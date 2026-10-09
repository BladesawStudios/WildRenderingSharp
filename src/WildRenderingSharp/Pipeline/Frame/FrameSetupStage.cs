using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Derives the camera, sun and draw groups for the frame and uploads the uniforms every pass shares.</summary>
public sealed class FrameSetupStage(FrameServices services) : IFrameStage
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
        frame.SunView = SunDirection.ToView(frame.SunWorld, cam.View);
        frame.SunColor = environment.SunColor;
        frame.HemiSky = environment.HemiSky;
        frame.HemiGround = environment.HemiGround;

        frame.GameOrigin = ClipOrigin.Supported(services.Gl);
        profile.Camera(FrameUniformKeys.SceneCamera, cam).Bind(services.Resources);
        profile.Camera(FrameUniformKeys.GBufferCamera, frame.GameOrigin ? cam : frame.FlippedCam).Bind(services.Resources);
        profile.Lighting(BuildSceneLighting(frame, environment)).Bind(services.Resources);

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
            casting.Add(new ActorDrawGroup(profile.InstancedActorPlaceholders, CameraData.Rows(Matrix4x4.Identity, 3),
                batch.Model.Shapes.Where(s => s.Enabled && (!s.Hidden || s.CastsShadow) && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList(), batch));

        frame.CastingGroups = casting;
        frame.Groups = [.. casting.Select(g => g with { Shapes = g.Shapes.Where(s => !s.Hidden).ToList() })];
        frame.OpaqueGroups = frame.Groups
            .Select(g => g with { Shapes = g.Shapes.Where(s => !s.Blend && !s.ReadsSceneColor).ToList() })
            .Where(g => g.Shapes.Count > 0).ToList();
    }
}
