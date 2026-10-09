using WildRenderingSharp.Graphics;
using WildRenderingSharp.Assets;
namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Draws the ground reference grid into the scene colour, depth-tested against the G-buffer.</summary>
public sealed class GridStage(FrameServices services, ForwardPass forward) : IFrameStage, IDisposable
{
    readonly GridPass _grid = new(services.Gl);

    public void Run(FrameContext frame)
    {
        if (!frame.Lighting.ShowGrid)
            return;

        var targets = frame.Targets;
        float sceneRadius = MathF.Max(1f, frame.Request.Actors.Select(a => a.Model.BoundsRadius)
            .Concat(frame.Instances.Select(b => b.Model.BoundsRadius)).DefaultIfEmpty(1f).Max());

        forward.FlipInto(services.Resources, targets, targets.Scene, targets.Final, flip: true);
        _grid.Run(targets, frame.FlippedCam.ViewProj, frame.Camera.Eye, MathF.Max(20f, sceneRadius * 20f));
        forward.FlipInto(services.Resources, targets, targets.Final, targets.Scene, flip: true);
        GLDiagnostics.CheckPass(services.Gl, "grid");
    }

    public void Dispose() => _grid.Dispose();
}
