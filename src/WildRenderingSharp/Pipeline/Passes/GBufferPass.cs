using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>Depth prepass (z-only, where a material has one) followed by the real G-buffer draw.</summary>
internal sealed class GBufferPass(GL gl)
{
    readonly List<(ulong Key, int Group, LoadedShape Shape)> _items = [];

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer)
    {
        targets.BindGBuffer();
        gl.ClearColor(0, 0, 0, 0);
        gl.ClearDepth(1.0);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Less);
        GpuPassTimer.On(gl)?.Mark("G-buffer setup");
        GpuPassTimer.On(gl)?.Detail("");

        drawer.BeginStateCache();
        DrawStages(resources, targets, groups, drawer);
        drawer.EndStateCache();
        gl.ActiveTexture(TextureUnit.Texture0);

        DrawStepDebugger(resources, targets, groups, drawer);
    }

    // A z-prepass pays only where the z-only program discards: that shape's G-buffer program takes its cutout from the prepass depth.
    static bool NeedsPrepass(ShapeDrawer drawer, LoadedShape shape) =>
        shape.HasZOnly && shape.RenderState.DepthWriteEnabled && drawer.Programs.FragmentDiscards(shape.ZOnlyShaderName);

    void DrawStages(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer)
    {
        var timer = GpuPassTimer.On(gl);
        bool Prepassed(LoadedShape s) => NeedsPrepass(drawer, s);

        if (groups.Any(g => g.Shapes.Any(Prepassed)))
        {
            targets.SetGBufferColorMask(false);
            DrawSorted(resources, groups, drawer, Prepassed, ShapeProgram.ZOnly, "z ");
            targets.SetGBufferColorMask(true);
            timer?.Mark("G-buffer z-prepass");

            gl.DepthFunc(DepthFunction.Equal);
            gl.DepthMask(false);
            DrawSorted(resources, groups, drawer, Prepassed, ShapeProgram.GBuffer, "");
            gl.DepthMask(true);
            timer?.Mark("G-buffer main");
        }

        gl.DepthFunc(DepthFunction.Less);
        DrawSorted(resources, groups, drawer, s => !Prepassed(s) && s.RenderState.DepthWriteEnabled, ShapeProgram.GBuffer, "no-z ");

        // Shapes that write no depth (a see-through surface the game blends into its G-buffer) draw last, tested but not written, and never in the prepass, where writing them hid what is behind.
        gl.DepthMask(false);
        DrawSorted(resources, groups, drawer, s => !s.RenderState.DepthWriteEnabled, ShapeProgram.GBuffer, "no-depth ");
        gl.DepthMask(true);
    }

    // An extra draw over just the shape being stepped, with depth testing forced to always pass: the normal draws only show fragments the
    // z-only prepass let through, which hides a value computed before a discard that fires. It runs last with depth writes off.
    void DrawStepDebugger(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer)
    {
        if (!groups.Any(g => g.Shapes.Any(s => s.DebugGBufferProgram is not null)))
            return;

        targets.SetGBufferColorMask(true);
        gl.DepthFunc(DepthFunction.Always);
        gl.DepthMask(false);
        foreach (var group in groups)
        {
            var debugging = group.Shapes.Where(s => s.DebugGBufferProgram is not null).ToList();
            if (group.Batch is not null || debugging.Count == 0)
                continue;

            group.BindUbos(resources);
            foreach (var shape in debugging)
            {
                uint program = shape.DebugGBufferProgram!.Value;
                // The debugger's program declares uDebugStepTarget and the real one does not, so it is set before the draw's own UseProgram.
                gl.UseProgram(program);
                gl.SetInt(program, "uDebugStepTarget", shape.DebugGBufferStepTarget);
                drawer.Draw(program, shape.GBufferVao, shape.MaterialBlock, shape.GBufferSamplers, shape.IndexCount, shape.SamplerOverrides);
            }
        }
        gl.DepthMask(true);
        gl.DepthFunc(DepthFunction.Less);
    }

    // Draws every group's shapes ordered by program then material, since re-binding per shape left the CPU behind the GPU.
    // A placed actor's shapes keep their order and go first, because their per-actor uniforms make them unsortable.
    void DrawSorted(GLResourceCache resources, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer,
        Func<LoadedShape, bool> include, ShapeProgram which, string detailPrefix)
    {
        CollectItems(groups, drawer, include, which);

        var timer = GpuPassTimer.On(gl);
        bool detailed = timer?.Detailed == true;
        int bound = -1;
        bool batchBlocks = false;
        foreach (var (_, g, shape) in _items)
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
            group.Draw(drawer, shape, which);
            if (detailed)
                timer!.Detail(detailPrefix + group.Label);
        }
    }

    void CollectItems(IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer, Func<LoadedShape, bool> include, ShapeProgram which)
    {
        _items.Clear();
        for (int g = 0; g < groups.Count; g++)
        {
            bool instanced = groups[g].Batch is { Visible.Count: > 0 };
            foreach (var shape in groups[g].Shapes.Where(include))
            {
                ulong key = 0;
                if (instanced)
                {
                    ActorDrawGroup.EnsureInstancedPrograms(drawer.Programs, shape);
                    uint program = which == ShapeProgram.ZOnly ? shape.InstancedZOnlyProgram : shape.InstancedGBufferProgram;
                    key = (1UL << 63) | ((ulong)program << 32) | shape.MaterialBlock.Handle;
                }
                _items.Add((key, g, shape));
            }
        }
        // Stable for the actors (key 0), whose order is the caller's.
        _items.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Group.CompareTo(b.Group));
    }
}
