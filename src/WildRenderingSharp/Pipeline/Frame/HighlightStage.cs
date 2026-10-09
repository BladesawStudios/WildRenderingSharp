using System.Numerics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Pipeline.Passes;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Overlays the requested shape on the finished image, ignoring depth so it shows through whatever hides it.</summary>
public sealed class HighlightStage(StageServices services) : IFrameStage, IDisposable
{
    static readonly Vector4 HighlightColor = new(1f, 0.85f, 0.2f, 0.2f);

    readonly HighlightOverlayPass _highlight = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var request = frame.Request;
        if (request.Highlight is not { } target
            || target.ActorIndex < 0 || target.ActorIndex >= request.Actors.Count
            || target.ShapeIndex < 0 || target.ShapeIndex >= request.Actors[target.ActorIndex].Model.Shapes.Count)
            return;

        var group = frame.Groups[target.ActorIndex];
        var shape = request.Actors[target.ActorIndex].Model.Shapes[target.ShapeIndex];
        var mvp = GpuMatrix.FromRows(group.ModelMatrixRows) * frame.MaskViewProj;
        _highlight.Draw(services.Resources, frame.Targets, group, shape, mvp, frame.MaskViewProj, HighlightColor);
        GLDiagnostics.CheckPass(services.Gl, "highlight overlay");
    }

    public void Dispose() => _highlight.Dispose();
}
