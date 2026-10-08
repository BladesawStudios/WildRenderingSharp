using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// A model is not one deferred pass: <c>o_material_behave</c> picks a different resolve program per material (the Master Sword is <c>chara_metal</c> throughout; a
/// character can span <c>chara_nonmetal</c>, <c>chara_hair</c>, <c>chara_skin</c> and <c>chara_grossy</c>). The game separates them with stencil; this stamps each
/// shape's pass as a small integer ID into an R8 target, and <see cref="DeferredResolvePass"/> composites each program only where the ID matches.
/// </summary>
/// <remarks>
/// The mask must be skinned, because the resolve is masked by it: a pixel the G-buffer wrote but the mask missed gets no resolve program and stays background.
/// Transforming raw positions was correct only while the exporter baked one bone pose into every vertex; once skinning moved to the GPU it made the mask a stencil of
/// the bind pose. The skinning below is the same operation as the G-buffer vertex shader.
/// </remarks>
public sealed class PassIdMaskPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    /// <summary>The same program for a batch of placements: patched like a game shader (see <see cref="InstancedShaderPatch"/>), with the rigid branch reading the placement from the instance buffer instead of the per-actor <c>uMVP</c>.</summary>
    readonly uint _instancedProgram;
    float _near, _far;

    /// <summary>
    /// Mirrors the compiled TotK vertex shader's skinning:
    ///   - blend indices are FLOAT attributes carrying an integer bit pattern, unpacked with <c>floatBitsToInt(v) &amp; 0xFFFF</c> (the real shader reads a second index
    ///     from the high half, which the exporter never packs);
    ///   - <c>_Mtx</c> at binding 2 is a flat <c>vec4</c> array, three rows per bone (48 bytes), each dotted with <c>vec4(pos, 1)</c> for one output component,
    ///     row-vector convention, as <see cref="Ubos.BonePaletteUbo"/> writes;
    ///   - a skinned draw does not apply the shape transform separately: the model matrix is folded into every palette entry, so skinned vertices go through
    ///     <c>uViewProj</c> and only <c>SKIN_COUNT == 0</c> (pose baked into the positions) uses <c>uMVP</c>.
    /// </summary>
    const string VertexSource = """
        #version 450 core
        layout (location = 0) in vec4 aPosition;
        layout (location = 4) in vec4 aBlendWeight0;
        layout (location = 5) in vec4 aBlendWeight1;
        layout (location = 6) in vec4 aBlendIndex0;
        layout (location = 7) in vec4 aBlendIndex1;

        layout (binding = 2, std140) uniform _Mtx { vec4 data[4096]; } bones;

        uniform mat4 uMVP;      // proj * view * model - SKIN_COUNT 0 only (positions are pre-posed)
        uniform mat4 uViewProj; // proj * view - the palette already carries the model transform
        uniform int uSkinCount;

        const int kBoneSlots = 4096 / 3; // the _Mtx array holds 3 vec4 rows per bone

        vec3 skinOne(vec3 p, float packedIndex)
        {
            // Clamped because a slot is only as wide as the palette built; a stray index would read past the block, which is undefined.
            int slot = clamp(floatBitsToInt(packedIndex) & 0xFFFF, 0, kBoneSlots - 1);
            vec4 v = vec4(p, 1.0);
            return vec3(dot(v, bones.data[slot * 3 + 0]),
                        dot(v, bones.data[slot * 3 + 1]),
                        dot(v, bones.data[slot * 3 + 2]));
        }

        void main()
        {
            vec3 p = aPosition.xyz;
            if (uSkinCount == 0)
            {
                gl_Position = uMVP * vec4(p, 1.0);
                return;
            }
            if (uSkinCount == 1)
            {
                // A single bind has no weight attribute in the real shader (the weight is an implicit 1.0), so the one slot is taken straight.
                gl_Position = uViewProj * vec4(skinOne(p, aBlendIndex0.x), 1.0);
                return;
            }
            vec3 skinned = skinOne(p, aBlendIndex0.x) * aBlendWeight0.x;
            skinned += skinOne(p, aBlendIndex0.y) * aBlendWeight0.y;
            if (uSkinCount >= 3) skinned += skinOne(p, aBlendIndex0.z) * aBlendWeight0.z;
            if (uSkinCount >= 4) skinned += skinOne(p, aBlendIndex0.w) * aBlendWeight0.w;
            if (uSkinCount >= 5) skinned += skinOne(p, aBlendIndex1.x) * aBlendWeight1.x;
            if (uSkinCount >= 6) skinned += skinOne(p, aBlendIndex1.y) * aBlendWeight1.y;
            if (uSkinCount >= 7) skinned += skinOne(p, aBlendIndex1.z) * aBlendWeight1.z;
            if (uSkinCount >= 8) skinned += skinOne(p, aBlendIndex1.w) * aBlendWeight1.w;
            gl_Position = uViewProj * vec4(skinned, 1.0);
        }
        """;

    /// <summary>
    /// Stamps the ID, but only where the G-buffer actually wrote a fragment. The mask needs exactly the G-buffer's visibility, and rasterising alone cannot give it: a
    /// mask-mode material's alpha cutout lives in its Z-only program's discard, which this pass lacks, so it stamped whole uncut quads (Ganondorf_Miasma's masked hair
    /// and miasma cards covered 47% more of the mask than the G-buffer and hid the eyes behind them).
    /// Rather than reproduce the cutout, it reads the answer the G-buffer computed, its depth: a fragment is stamped only where its depth matches the G-buffer's, i.e.
    /// where this shape is the surface the G-buffer kept, which inherits the cutout and the occlusion and lets whatever shows through a hole stamp its own ID.
    /// Albedo alpha was tried instead, but it is a flag field (bit 0 gates emission) that 73 of a map section's G-buffer programs write as 0, so over half its static
    /// objects got no ID and came out black.
    /// The G-buffer is rasterised through the flipped projection and this pass through the true one, so the matching texel is at 1 - y (as in the compose step of
    /// <c>DeferredResolvePass</c>); the flip changes only y, so depths compare directly, with a tolerance for this pass's skinning not matching the game's instruction for instruction.
    /// </summary>
    const string FragmentSource = """
        #version 450 core
        uniform float uId;
        uniform sampler2D tex_gbuf_depth;
        uniform vec2 uInvViewport;
        uniform vec2 uNearFar;
        layout (location = 0) out vec4 fragColor;
        float viewDepth(float d) {
            float n = uNearFar.x, f = uNearFar.y;
            return (2.0 * n * f) / (f + n - (d * 2.0 - 1.0) * (f - n));
        }
        void main() {
            vec2 g = vec2(gl_FragCoord.x * uInvViewport.x, 1.0 - gl_FragCoord.y * uInvViewport.y);
            float kept = texture(tex_gbuf_depth, g).r;
            if (kept >= 1.0)
                discard;
            float zKept = viewDepth(kept), zThis = viewDepth(gl_FragCoord.z);
            if (abs(zKept - zThis) > max(0.02, zKept * 0.002))
                discard;
            fragColor = vec4(uId, 0.0, 0.0, 1.0);
        }
        """;

    public PassIdMaskPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "pass_id_mask");
        string instanced = InstancedShaderPatch.Apply(VertexSource.Replace(
            "gl_Position = uMVP * vec4(p, 1.0);",
            "vec4 v0 = vec4(p, 1.0); gl_Position = uViewProj * vec4(dot(v0, wrs_shp(0)), dot(v0, wrs_shp(1)), dot(v0, wrs_shp(2)), 1.0);"))
            ?? throw new InvalidOperationException("pass_id_mask has no main to instance");
        _instancedProgram = GLProgramBuilder.Build(gl, instanced, FragmentSource, "pass_id_mask_instanced");
    }

    /// <summary>Ordered distinct deferred-pass names present in a shape list - pass i owns ID (i + 1) / 255.</summary>
    static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }

    public static List<string> DistinctPasses(IEnumerable<LoadedShape> shapes) =>
        shapes.Select(s => s.DeferredPass)
              .Where(p => !string.IsNullOrEmpty(p))
              .Distinct()
              .OrderBy(p => p, StringComparer.Ordinal)
              .ToList();

    /// <summary>Both matrices use the unflipped projection: the resolve consumes the ID buffer in true GL (lower-left) orientation, not the G-buffer's flipped one.</summary>
    /// <param name="viewProjRows"><c>proj @ [view;0,0,0,1]</c>, shared across actors. Skinned shapes use it directly (their palette carries the actor's model transform); each actor's skin-count-0 shapes combine it with that actor's own model rows.</param>
    public unsafe void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, IReadOnlyList<string> passes,
        ReadOnlySpan<Vector4> viewProjRows, float near, float far)
    {
        targets.BindPassIdTarget();
        _gl.ClearColor(0, 0, 0, 1);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        // Visibility comes from the G-buffer's depth (see FragmentSource), not a test of this pass's own.
        _gl.Disable(EnableCap.DepthTest);
        _near = near;
        _far = far;

        _gl.UseProgram(_program);
        _gl.SetMat4(_program, "uViewProj", viewProjRows);
        _gl.BindTextureUniform(_program, "tex_gbuf_depth", 0, targets.GBufferDepth.Handle);
        _gl.SetVec2(_program, "uInvViewport", new Vector2(1f / targets.Width, 1f / targets.Height));
        _gl.SetVec2(_program, "uNearFar", new Vector2(near, far));
        int idLocation = _gl.GetUniformLocation(_program, "uId");
        int skinLocation = _gl.GetUniformLocation(_program, "uSkinCount");

        foreach (var group in groups.Where(g => g.Batch is null))
        {
            group.BindUbos(resources);
            var mvp = Mat4Math.Multiply(viewProjRows, Mat4Math.ToMat4(group.ModelMatrixRows));
            _gl.SetMat4(_program, "uMVP", mvp);

            foreach (var sh in group.Shapes)
            {
                if (string.IsNullOrEmpty(sh.DeferredPass))
                    continue;
                int index = IndexOf(passes, sh.DeferredPass);
                if (index < 0)
                    continue;
                _gl.Uniform1(idLocation, (index + 1) / 255f);
                _gl.Uniform1(skinLocation, sh.VertexSkinCount);
                _gl.BindVertexArray(sh.PassIdVao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)sh.IndexCount, DrawElementsType.UnsignedInt, null);
            }
        }
        RunInstanced(resources, targets, groups, passes, viewProjRows);
        _gl.Disable(EnableCap.DepthTest);
    }

    unsafe void RunInstanced(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, IReadOnlyList<string> passes,
        ReadOnlySpan<Vector4> viewProjRows)
    {
        if (!groups.Any(g => g.Batch is { Visible.Count: > 0 }))
            return;

        uint program = _instancedProgram;
        _gl.UseProgram(program);
        _gl.SetMat4(program, "uViewProj", viewProjRows);
        _gl.BindTextureUniform(program, "tex_gbuf_depth", 0, targets.GBufferDepth.Handle);
        _gl.SetVec2(program, "uInvViewport", new Vector2(1f / targets.Width, 1f / targets.Height));
        _gl.SetVec2(program, "uNearFar", new Vector2(_near, _far));
        int id = _gl.GetUniformLocation(program, "uId");
        int skin = _gl.GetUniformLocation(program, "uSkinCount");
        int first = _gl.GetUniformLocation(program, InstancingContract.FirstInstanceUniform);
        int stride = _gl.GetUniformLocation(program, InstancingContract.StrideUniform);
        int palette = _gl.GetUniformLocation(program, InstancingContract.PaletteVec4sUniform);
        int repeat = _gl.GetUniformLocation(program, InstancingContract.PaletteRepeatUniform);

        foreach (var group in groups)
        {
            if (group.Batch is not { Visible.Count: > 0 } batch)
                continue;
            group.BindUbos(resources);
            _gl.Uniform1(stride, batch.Stride);
            _gl.Uniform1(palette, batch.PaletteVec4s);
            _gl.Uniform1(repeat, batch.PaletteRepeats ? 1 : 0);

            foreach (var sh in group.Shapes)
            {
                if (string.IsNullOrEmpty(sh.DeferredPass))
                    continue;
                int index = IndexOf(passes, sh.DeferredPass);
                if (index < 0)
                    continue;
                _gl.Uniform1(id, (index + 1) / 255f);
                _gl.Uniform1(skin, sh.VertexSkinCount);
                _gl.BindVertexArray(sh.PassIdVao);
                // One multi-draw for every visible run, as the G-buffer does; run by run was thousands of calls a frame.
                _gl.Uniform1(first, 0);
                if (ShapeDrawing.MultiDrawRuns(_gl, sh, batch.Visible))
                    continue;
                foreach (var (start, count, lod) in batch.Visible)
                {
                    var (firstIndex, indexCount) = sh.Lod(lod);
                    if (count <= 0 || indexCount <= 0)
                        continue;
                    _gl.Uniform1(first, start);
                    _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt,
                        (void*)((nint)firstIndex * sizeof(uint)), (uint)count);
                }
            }
        }
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_program);
        _gl.DeleteProgram(_instancedProgram);
    }
}
