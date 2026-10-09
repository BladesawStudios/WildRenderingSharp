using WildRenderingSharp.Graphics;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// A model is not one deferred pass: <c>o_material_behave</c> picks a different resolve program per material (the Master Sword is
/// <c>chara_metal</c> throughout; a character can span <c>chara_nonmetal</c>, <c>chara_hair</c>, <c>chara_skin</c> and
/// <c>chara_grossy</c>).
/// </summary>
public sealed class PassIdMaskPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    // The same program for a batch of placements: patched like a game shader (see InstancedShaderPatch), with the rigid branch
    // reading the placement from the instance buffer instead of the per-actor uMVP.
    readonly uint _instancedProgram;
    float _near, _far;

    // Mirrors the compiled TotK vertex shader's skinning: - blend indices are FLOAT attributes carrying an integer bit pattern,
    // unpacked with floatBitsToInt(v) & 0xFFFF (the real shader reads a second index from the high half, which the exporter
    // never packs); - _Mtx at binding 2 is a flat vec4 array, three rows per bone (48 bytes), each dotted with vec4(pos, 1) for
    // one output component, row-vector convention, as BonePaletteUbo writes; - a skinned draw does not apply the shape
    // transform separately: the model matrix is folded into every palette entry, so skinned vertices go through uViewProj and
    // only SKIN_COUNT == 0 (pose baked into the positions) uses uMVP.
    static readonly string VertexSource = GlslFiles.Load("Totk/Deferred/PassIdMask/Main.vert");

    // Stamps the ID, but only where the G-buffer actually wrote a fragment. The mask needs exactly the G-buffer's visibility,
    // and rasterising alone cannot give it: a mask-mode material's alpha cutout lives in its Z-only program's discard, which
    // this pass lacks, so it stamped whole uncut quads (Ganondorf_Miasma's masked hair and miasma cards covered 47% more of the
    // mask than the G-buffer and hid the eyes behind them). Rather than reproduce the cutout, it reads the answer the G-buffer
    // computed, its depth: a fragment is stamped only where its depth matches the G-buffer's, i.e. where this shape is the
    // surface the G-buffer kept, which inherits the cutout and the occlusion and lets whatever shows through a hole stamp its
    // own ID.
    static readonly string FragmentSource = GlslFiles.Load("Totk/Deferred/PassIdMask/Main.frag");

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

    static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }

    public static List<string> DistinctPasses(IEnumerable<LoadedShape> shapes) =>
        shapes.Select(s => s.DeferredPass())
              .Where(p => !string.IsNullOrEmpty(p))
              .Distinct()
              .OrderBy(p => p, StringComparer.Ordinal)
              .ToList();

    public unsafe void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, IReadOnlyList<string> passes,
        Matrix4x4 viewProj, float near, float far, int claimPass = -1)
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
        _gl.SetMat4(_program, "uViewProj", viewProj);
        _gl.BindTextureUniform(_program, "tex_gbuf_depth", 0, targets.GBufferDepth.Handle);
        _gl.SetVec2(_program, "uInvViewport", new Vector2(1f / targets.Width, 1f / targets.Height));
        _gl.SetVec2(_program, "uNearFar", new Vector2(near, far));
        int idLocation = _gl.GetUniformLocation(_program, "uId");
        int skinLocation = _gl.GetUniformLocation(_program, "uSkinCount");

        foreach (var group in groups.Where(g => g.Batch is null))
        {
            group.BindUbos(resources);
            var mvp = GpuMatrix.FromRows(group.ModelMatrixRows) * viewProj;
            _gl.SetMat4(_program, "uMVP", mvp);

            foreach (var sh in group.Shapes)
            {
                if (string.IsNullOrEmpty(sh.DeferredPass()))
                    continue;
                int index = IndexOf(passes, sh.DeferredPass());
                if (index < 0 || index == claimPass)
                    continue;
                _gl.Uniform1(idLocation, (index + 1) / 255f);
                _gl.Uniform1(skinLocation, sh.VertexSkinCount);
                _gl.BindVertexArray(sh.PassIdVao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)sh.IndexCount, DrawElementsType.UnsignedInt, null);
            }
        }
        RunInstanced(resources, targets, groups, passes, viewProj, claimPass);
        _gl.Disable(EnableCap.DepthTest);
    }

    unsafe void RunInstanced(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, IReadOnlyList<string> passes,
        Matrix4x4 viewProj, int claimPass)
    {
        if (!groups.Any(g => g.Batch is { Visible.Count: > 0 }))
            return;

        uint program = _instancedProgram;
        _gl.UseProgram(program);
        _gl.SetMat4(program, "uViewProj", viewProj);
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
                if (string.IsNullOrEmpty(sh.DeferredPass()))
                    continue;
                int index = IndexOf(passes, sh.DeferredPass());
                if (index < 0 || index == claimPass)
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
