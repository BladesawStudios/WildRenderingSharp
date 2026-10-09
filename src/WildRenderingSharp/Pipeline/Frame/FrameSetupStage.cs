using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Derives the camera, sun and draw groups for the frame and uploads the uniforms every pass shares.</summary>
public sealed class FrameSetupStage(StageServices services) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        var profile = services.Profile;
        var lighting = frame.Lighting;
        var environment = frame.Environment.ResolveLighting(lighting);

        var cam = CameraData.From(frame.Camera, frame.Targets.Width, frame.Targets.Height);
        frame.Cam = cam;
        frame.FlippedCam = cam.FlippedY();
        frame.MaskViewProj = cam.ViewProj;

        frame.SunWorld = SunDirection.FromElevationAzimuth(lighting.SunElevation, lighting.SunAzimuth);
        frame.SunView = Vector3.Transform(frame.SunWorld, cam.View);
        frame.SunColor = environment.SunColor;
        frame.HemiSky = environment.HemiSky;
        frame.HemiGround = environment.HemiGround;

        frame.GameOrigin = ClipOrigin.Supported(services.Gl);
        services.Resources.Bind(profile.Camera(FrameUniformKeys.SceneCamera, cam));
        services.Resources.Bind(profile.Camera(FrameUniformKeys.GBufferCamera, frame.GameOrigin ? cam : frame.FlippedCam));
        services.Resources.Bind(profile.Lighting(BuildSceneLighting(frame, environment)));

        BuildGroups(frame);
    }

    static SceneLightingData BuildSceneLighting(FrameContext frame, EnvironmentLighting environment) =>
        new(frame.SunView, frame.SunWorld, environment.SunColor, environment.HemiSky, environment.HemiGround,
            environment.VolumeMaskColor, environment.VolumeMaskIntensity, RenderTargets.ShadowMapSize,
            frame.Lighting.MidScale, frame.Lighting.HighlightScale);

    // Each actor gets its own group, with uniforms built fresh from its placement and pose, and its shapes filtered fresh so
    // toggling a shape takes effect on the next frame.
    void BuildGroups(FrameContext frame)
    {
        var profile = services.Profile;

        var casting = frame.Request.Actors.Select(a => new ActorDrawGroup(
            profile.Actor(new SkinningData(a.ModelMatrixRows, a.Model.Skeleton, a.BoneWorldMatrices)),
            a.ModelMatrixRows,
            a.Model.Shapes.Where(s => s.Enabled && (!s.Hidden || s.CastsShadow)).ToList())).ToList();

        foreach (var batch in frame.Instances.Where(b => b.Visible.Count > 0))
            casting.Add(ActorDrawGroup.ForBatch(profile, batch,
                batch.Model.Shapes.Where(s => s.Enabled && (!s.Hidden || s.CastsShadow) && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList()));

        frame.CastingGroups = casting;
        frame.Groups = [.. casting.Select(g => g with { Shapes = g.Shapes.Where(s => !s.Hidden).ToList() })];
        frame.OpaqueGroups = frame.Groups
            .Select(g => g with { Shapes = g.Shapes.Where(s => !s.Blend && !s.ReadsSceneColor).ToList() })
            .Where(g => g.Shapes.Count > 0).ToList();
    }
}
