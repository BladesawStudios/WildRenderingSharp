using WildRenderingSharp.Graphics;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Repairs the few materials the translated shaders draw wrongly.</summary>
public sealed class KnownMaterialFixesStage(FrameServices services, DeferredScene scene, ForwardPass forward) : IFrameStage, IDisposable
{
    readonly KnownMaterialFixes _fixes = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        if (!frame.TotkEnvironment().Settings.EnableKnownMaterialFixes || !scene.NeedsKnownMaterialFixes)
            return;

        var targets = frame.Targets;
        forward.FlipInto(services.Resources, targets, targets.Scene, targets.Final, flip: true);
        _fixes.Run(services.Resources, targets, frame.OpaqueGroups, services.Profile.World.Rows(CameraData.Rows(frame.FlippedCam.ViewProj)),
            lighting.EmissionScale, lighting.Exposure);
        forward.FlipInto(services.Resources, targets, targets.Final, targets.Scene, flip: true);
        GLDiagnostics.CheckPass(services.Gl, "known material fixes");
    }

    public void Dispose() => _fixes.Dispose();
}
