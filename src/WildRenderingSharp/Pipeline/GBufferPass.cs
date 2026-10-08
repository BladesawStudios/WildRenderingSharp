using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Depth prepass (z-only, where a material has one) followed by the real G-buffer draw. The prepass carries the alpha-test discard several materials' G-buffer program lacks; the G-buffer draw then
/// runs with depth func EQUAL so a texel the prepass cut away has no matching depth and is rejected, which is the engine's own opaque flow.
/// </summary>
/// <remarks>
/// The caller must have the flipped-projection <c>Context</c> bound before <see cref="Run"/> (the G-buffer renders through NVN's upper-left-origin convention; later passes undo that).
/// Every placed actor's opaque shapes go into the same G-buffer and depth target, so each occludes every other, with each actor's own skinning uniforms (<see cref="ActorDrawGroup"/>) rebound
/// immediately before its shapes. The prepass-then-EQUAL technique spans every actor as one shared depth pass (all prepasses first, then all real draws), which is what keeps depth correct between actors.
/// </remarks>
public sealed class GBufferPass
{
    readonly GL _gl;

    public GBufferPass(GL gl) => _gl = gl;

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShaderProgramCache programs)
    {
        targets.BindGBuffer();
        _gl.ClearColor(0, 0, 0, 0);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);

        var timer = GpuPassTimer.Current;
        timer?.Mark("G-buffer setup");
        timer?.Detail("");
        bool anyZOnly = groups.Any(g => g.Shapes.Any(s => s.HasZOnly && s.RenderState.DepthWriteEnabled));
        ShapeDrawing.BeginStateCache();
        if (anyZOnly)
        {
            targets.SetGBufferColorMask(false);
            DrawSorted(resources, groups, programs, s => s.HasZOnly && s.RenderState.DepthWriteEnabled, ShapeProgram.ZOnly, "z ");
            targets.SetGBufferColorMask(true);
            timer?.Mark("G-buffer z-prepass");

            _gl.DepthFunc(DepthFunction.Equal);
            _gl.DepthMask(false);
            DrawSorted(resources, groups, programs, s => s.HasZOnly && s.RenderState.DepthWriteEnabled, ShapeProgram.GBuffer, "");
            _gl.DepthMask(true);
            timer?.Mark("G-buffer main");
        }

        _gl.DepthFunc(DepthFunction.Less);
        DrawSorted(resources, groups, programs, s => !s.HasZOnly && s.RenderState.DepthWriteEnabled, ShapeProgram.GBuffer, "no-z ");

        // Shapes whose render state writes no depth (a see-through surface the game blends into its G-buffer, like a shrine's warp-hole aura) draw after everything that does, tested but not written,
        // and never in the prepass, where writing them hid what is behind.
        _gl.DepthMask(false);
        DrawSorted(resources, groups, programs, s => !s.RenderState.DepthWriteEnabled, ShapeProgram.GBuffer, "no-depth ");
        _gl.DepthMask(true);
        ShapeDrawing.EndStateCache();
        _gl.ActiveTexture(TextureUnit.Texture0);

        // Shader step debugger, G-buffer target: an extra draw over just the shape being stepped, with depth testing forced to always-pass. The normal draws only show fragments the Z-only prepass let
        // through, so a value computed before an alpha-test discard that fires is invisible through them (see LoadedShape.DebugGBufferProgram). It runs last with depth writes off, so it cannot affect anything else.
        bool anyDebugging = groups.Any(g => g.Shapes.Any(s => s.DebugGBufferProgram is not null));
        if (anyDebugging)
        {
            targets.SetGBufferColorMask(true);
            _gl.DepthFunc(DepthFunction.Always);
            _gl.DepthMask(false);
            foreach (var group in groups)
            {
                var debugging = group.Shapes.Where(s => s.DebugGBufferProgram is not null);
                if (group.Batch is not null || !debugging.Any())
                    continue;
                group.BindUbos(resources);
                foreach (var sh in debugging)
                {
                    uint program = sh.DebugGBufferProgram!.Value;
                    // The step debugger's program declares uDebugStepTarget and the real GBufferProgram does not, so it is set before Draw's own UseProgram.
                    _gl.UseProgram(program);
                    _gl.SetInt(program, "uDebugStepTarget", sh.DebugGBufferStepTarget);
                    ShapeDrawing.Draw(_gl, programs.Bindings.Material, program, sh.GBufferVao, sh.MaterialBuffer, sh.GBufferSamplers, sh.IndexCount, sh.SamplerOverrides);
                }
            }
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Less);
        }
    }

    readonly List<(ulong Key, int Group, LoadedShape Shape)> _items = [];

    /// <summary>Draws the shapes <paramref name="include"/> picks, every group's together, ordered by program and then material: hundreds of models share a few hundred programs, and drawn model by model each re-bound its program, uniforms and textures per shape, so the CPU spent longer issuing a frame than the card spent drawing it. A placed actor's shapes keep their order and go first; their per-actor uniforms make them unsortable.</summary>
    void DrawSorted(GLResourceCache resources, IReadOnlyList<ActorDrawGroup> groups, ShaderProgramCache programs,
        Func<LoadedShape, bool> include, ShapeProgram which, string detailPrefix)
    {
        _items.Clear();
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            bool instanced = group.Batch is { Visible.Count: > 0 };
            foreach (var sh in group.Shapes)
            {
                if (!include(sh))
                    continue;
                ulong key = 0;
                if (instanced)
                {
                    ActorDrawGroup.EnsureInstancedPrograms(programs, sh);
                    uint program = which == ShapeProgram.ZOnly ? sh.InstancedZOnlyProgram : sh.InstancedGBufferProgram;
                    key = (1UL << 63) | ((ulong)program << 32) | sh.MaterialBuffer;
                }
                _items.Add((key, g, sh));
            }
        }
        // Stable for the actors (key 0), whose order is the caller's.
        _items.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Group.CompareTo(b.Group));

        var timer = GpuPassTimer.Current;
        bool detailed = timer?.Detailed == true;
        int bound = -1;
        bool batchBlocks = false;
        foreach (var (_, g, sh) in _items)
        {
            var group = groups[g];
            if (g != bound)
            {
                if (group.Batch is not null && batchBlocks)
                    group.BindInstanceBuffer(resources);
                else
                    group.BindUbos(resources);
                batchBlocks = group.Batch is not null;
                bound = g;
            }
            group.Draw(_gl, programs, sh, which);
            if (detailed) timer!.Detail(detailPrefix + group.Label);
        }
    }
}
