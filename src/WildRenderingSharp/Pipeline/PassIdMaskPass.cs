using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// A model is not one deferred pass - <c>o_material_behave</c> picks a DIFFERENT resolve program
/// per material (the Master Sword is <c>chara_metal</c> throughout, but a character model can
/// span <c>chara_nonmetal</c>/<c>chara_hair</c>/<c>chara_skin</c>/<c>chara_grossy</c>). The game
/// separates these with stencil; this stamps each shape's resolved pass name as a small integer
/// ID into an R8 target instead, and <see cref="DeferredResolvePass"/> composites each resolve
/// program only where the ID matches. Mirrors <c>load_pass_masks</c>/<c>draw_pass_masks</c>.
///
/// THE MASK MUST BE SKINNED, because the resolve is masked BY it: a pixel the G-buffer wrote but
/// the mask missed gets no resolve program at all and stays background. This pass used to
/// transform raw positions by a plain model-view-projection, which was correct only while the
/// exporter baked one fixed bone pose into every vertex. Once skinning moved to the GPU that made
/// the mask a stencil of the BIND pose - so an animated model rendered as the intersection of its
/// animated silhouette and its bind-pose silhouette, looking exactly like a bind-pose cutout the
/// moving mesh was visible through. The skinning below is the same operation the real G-buffer
/// vertex shader performs, so the mask keeps exactly the G-buffer's visibility again.
/// </summary>
public sealed class PassIdMaskPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    /// <summary>
    /// The same program for a batch of placements: patched like a game shader
    /// (<see cref="InstancedShaderPatch"/>), with the rigid branch reading the placement out of the
    /// instance buffer rather than the per-actor <c>uMVP</c>.
    /// </summary>
    readonly uint _instancedProgram;
    float _near, _far;

    /// <summary>
    /// Mirrors the compiled TotK vertex shader's own skinning (see <c>Shaders/TOTK/Vertex.vert</c>'s
    /// <c>skin()</c> and any decompiled <c>*_extracted.vert</c>):
    ///   - blend indices arrive as FLOAT attributes carrying an integer bit pattern, unpacked with
    ///     <c>floatBitsToInt(v) &amp; 0xFFFF</c> (the real shader also reads a second index out of
    ///     the high half, which this codebase's exporter never packs, so it stays zero);
    ///   - <c>_Mtx</c> at binding 2 is one flat <c>vec4</c> array, three consecutive rows per bone
    ///     (48 bytes), each row dotted with <c>vec4(pos, 1)</c> to produce one output component -
    ///     row-vector convention, exactly what <see cref="WildRenderingSharp.Shaders.Profiles.Totk.Ubos.BonePaletteUbo"/> writes;
    ///   - a skinned draw does NOT apply the shape transform separately: the model matrix is already
    ///     folded into every palette entry, so skinned vertices go through <c>uViewProj</c> while
    ///     only <c>SKIN_COUNT == 0</c> (bone pose baked into the exported positions) uses <c>uMVP</c>.
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
            // Clamped because a slot is only ever as wide as the palette actually built; a stray
            // index would otherwise read past the block, which is undefined rather than merely wrong.
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
                // A single bind has no weight attribute in the real compiled shader - the weight is
                // an implicit 1.0 - so take the one slot straight rather than trusting a weight.
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
    /// Stamps the ID, but ONLY where the G-buffer actually wrote a fragment.
    ///
    /// The mask has to have exactly the G-buffer's visibility, and it cannot get there by
    /// rasterising alone: a mask-mode material's alpha cutout lives in its Z-ONLY program's
    /// discard (its G-buffer program has none - the G-buffer draw relies on the depth prepass
    /// having already cut those texels away), and this pass has neither program. Left to itself it
    /// stamps whole uncut quads, which is how Ganondorf_Miasma's masked hair and miasma cards came
    /// to cover 47% more of the mask than the G-buffer and hide the eyes and secret stone behind
    /// them - shapes that were in the G-buffer, got no ID, and so were discarded by the resolve.
    ///
    /// Rather than reproduce the cutout (which would mean knowing each material's alpha texture and
    /// threshold, and matching a second program's depth bit-for-bit), read the answer the G-buffer
    /// already computed: its DEPTH. A fragment is stamped only where its own depth matches the
    /// G-buffer's, i.e. where this shape is the surface the G-buffer actually kept - which inherits
    /// the cutout and the occlusion exactly, and lets whatever shows through a cutout's holes stamp
    /// its own ID there, since this pass does no depth test of its own.
    ///
    /// This used to read the albedo attachment's ALPHA instead, on the belief that every material
    /// writes a nonzero value there. It is a flag field (bit 0 gates emission, see
    /// <c>DeferredResolvePass</c>), and 73 of the G-buffer programs a map section uses write 0 -
    /// over half its static objects - so they got no ID, the resolve never shaded them, and they
    /// came out black.
    ///
    /// The G-buffer is rasterised through the flipped projection and this pass through the true
    /// one, so the matching G-buffer texel for this fragment is at 1 - y, the same flip
    /// <c>DeferredResolvePass</c>'s compose already applies for the same reason. The flip changes
    /// only y, never depth, so the two depths are directly comparable; the tolerance covers this
    /// pass's own skinning not being the game program's instruction for instruction.
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
    public static List<string> DistinctPasses(IEnumerable<LoadedShape> shapes) =>
        shapes.Select(s => s.DeferredPass)
              .Where(p => !string.IsNullOrEmpty(p))
              .Distinct()
              .OrderBy(p => p, StringComparer.Ordinal)
              .ToList();

    /// <summary>
    /// Both matrices use the UNflipped projection - the ID buffer is consumed by the resolve in the
    /// resolve's own (true GL, lower-left) orientation, not the G-buffer's Y-flipped one, so it must
    /// be drawn in that same space.
    /// </summary>
    /// <param name="viewProjRows"><c>proj @ [view;0,0,0,1]</c> - shared across every actor (camera-only); used directly by skinned shapes (whose palette already includes their own actor's model transform) and combined with each actor's OWN model rows for that actor's skin-count-0 shapes.</param>
    public unsafe void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, List<string> passes,
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
                int index = passes.IndexOf(sh.DeferredPass);
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

    unsafe void RunInstanced(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, List<string> passes,
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
        int first = _gl.GetUniformLocation(program, InstancedShaderPatch.FirstInstanceUniform);
        int stride = _gl.GetUniformLocation(program, InstancedShaderPatch.StrideUniform);
        int palette = _gl.GetUniformLocation(program, InstancedShaderPatch.PaletteVec4sUniform);
        int repeat = _gl.GetUniformLocation(program, InstancedShaderPatch.PaletteRepeatUniform);

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
                int index = passes.IndexOf(sh.DeferredPass);
                if (index < 0)
                    continue;
                _gl.Uniform1(id, (index + 1) / 255f);
                _gl.Uniform1(skin, sh.VertexSkinCount);
                _gl.BindVertexArray(sh.PassIdVao);
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
