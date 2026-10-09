using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>Draws blended materials forward after the deferred resolve, as the engine does with <c>gsys_assign_material</c> once the scene behind them is resolved.</summary>
public sealed class ForwardPass(GL gl, FlipBlit flip, ForwardNeutralInputs neutral)
{
    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer)
    {
        var forwardGroups = groups
            .Select(g => (Group: g, Forward: g.Shapes.Where(s => s.HasForward && (s.Blend || s.ForceForward)).ToList()))
            .Where(x => x.Forward.Count > 0)
            .ToList();
        if (forwardGroups.Count == 0)
            return;

        Begin(resources, targets);
        foreach (var (group, shapes) in forwardGroups)
        {
            group.BindUbos(resources);
            foreach (var shape in shapes)
                DrawShape(group, shape, drawer);
        }
        End(resources, targets);
    }

    // Draws into the scene in the G-buffer's flipped orientation, against the G-buffer's depth, with a copy of the scene behind for cTex_ColorBuffer.
    void Begin(GLResourceCache resources, RenderTargets targets)
    {
        gl.Disable(EnableCap.Blend);
        flip.Copy(resources, targets, targets.Scene, targets.Final, flip: true);
        flip.Copy(resources, targets, targets.Behind, targets.Scene, flip: false);

        targets.BindColorAndDepthTarget(targets.Scene, targets.GBufferDepth);
        gl.Enable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);

        // The shared camera block must be the flipped one, or the shape is transformed unflipped while everything around it is flipped.
        resources.BindCamera(FrameUniformKeys.GBufferCamera);
        ClipOrigin.Game(gl, true);

        gl.BindTextureAt(5, targets.LinearDepthHalf.Handle);
        gl.BindTextureAt(30, targets.Behind.Handle);
        neutral.Bind();
    }

    void End(GLResourceCache resources, RenderTargets targets)
    {
        gl.Disable(EnableCap.Blend);
        // GL keeps the blend equation as global state even with blending off, so a material authoring sub, min or max would leak it.
        gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.DepthMask(true);
        gl.DepthFunc(DepthFunction.Less);
        gl.Disable(EnableCap.DepthTest);
        ClipOrigin.Game(gl, false);
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        flip.CopyWithFloor(resources, targets, targets.Final, targets.Scene, targets.Behind, flip: true);
    }

    void DrawShape(ActorDrawGroup group, LoadedShape shape, ShapeDrawer drawer)
    {
        SetBlend(shape);
        gl.DepthFunc((DepthFunction)shape.RenderState.ResolveDepthFunc());
        gl.DepthMask(shape.RenderState.DepthWriteEnabled);
        SetPolygonOffset(shape);

        if (group.Batch is not null)
        {
            group.Draw(drawer, shape, ShapeProgram.Forward);
            return;
        }
        uint program = shape.ForwardProgram;
        if (shape.DebugForwardProgram is { } debugProgram)
        {
            // The debugger's program declares uDebugStepTarget and the real one does not, so it is set before the draw's own UseProgram.
            program = debugProgram;
            gl.UseProgram(program);
            gl.SetInt(program, "uDebugStepTarget", shape.DebugStepTarget);
        }
        drawer.Draw(program, shape.ForwardVao, shape.MaterialBlock, shape.ForwardSamplers, shape.IndexCount, shape.SamplerOverrides);
    }

    // A blended shape uses its own blend state. An opaque or masked one redraws additively for its status-effect overlay: its forward
    // program outputs a delta to composite over the deferred result, and replacing that result left a flat black hand with a glowing rim.
    void SetBlend(LoadedShape shape)
    {
        gl.Enable(EnableCap.Blend);
        if (shape.Blend)
        {
            var (funcs, ops) = shape.RenderState.ResolveBlendState();
            gl.BlendFuncSeparate(funcs.SrcRgb, funcs.DstRgb, funcs.SrcAlpha, funcs.DstAlpha);
            gl.BlendEquationSeparate(ops.Rgb, ops.Alpha);
            return;
        }
        gl.BlendFuncSeparate(GLEnum.One, GLEnum.One, GLEnum.One, GLEnum.One);
        gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
    }

    // The redraw depth-tests against depth the G-buffer pass wrote through a separately compiled vertex program, which can land a few
    // ULPs apart. A small negative offset pulls it toward the camera so the test passes at any distance; blended shapes redraw no resolved silhouette.
    void SetPolygonOffset(LoadedShape shape)
    {
        if (shape.Blend)
        {
            gl.Disable(EnableCap.PolygonOffsetFill);
            return;
        }
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(-1f, -1f);
    }
}
