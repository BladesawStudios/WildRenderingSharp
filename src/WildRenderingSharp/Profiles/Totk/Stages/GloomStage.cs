using WildRenderingSharp.Gpu;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Gloom;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Composes the host's gloom into the G-buffer, over everything drawn into it, so the game's miasma pass lights it with the rest.</summary>
/// <remarks>Without the game's window origin the game's programs would draw upside down, so the stage draws nothing then.</remarks>
internal sealed class GloomStage(StageServices services, DeferredScene scene, LinearDepthPass linearDepth) : IFrameStage, IDisposable
{
    readonly GloomPass _gloom = new(services.Gl, services.Programs);
    bool _logged;

    public void Run(FrameContext frame)
    {
        var environment = frame.TotkEnvironment();
        if (environment.Gloom is not { Textures: { } textures } source || !environment.Settings.UseGloom || !_gloom.Available || !frame.Setup.GameOrigin)
            return;
        if (source.Volumes.Count == 0 && source.AltitudeDensity is null)
            return;

        if (!_logged)
        {
            _logged = true;
            Log.Info($"[GloomStage] first frame: {source.Volumes.Count} volumes, altitude density {source.AltitudeDensity?.ToString("G3") ?? "none"}");
        }
        scene.EnsurePass(DeferredScene.MiasmaPass);

        // The terrain and the entrance interior have changed the depth since the last time it was made linear.
        linearDepth.Run(services.Resources, frame.Targets, frame.Camera.NearPlane, frame.Camera.FarPlane);
        ClipOrigin.Game(services.Gl, true);
        try
        {
            _gloom.Run(services.Resources, frame.Targets, frame.Setup.Cam, source, textures);
        }
        finally
        {
            // Whatever happened, the frame's other passes and the host draw with the ordinary window origin.
            ClipOrigin.Game(services.Gl, false);
        }
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
        frame.Stats.GloomComposed = true;
        GLDiagnostics.CheckPass(services.Gl, "gloom");
    }

    public void Dispose() => _gloom.Dispose();
}
