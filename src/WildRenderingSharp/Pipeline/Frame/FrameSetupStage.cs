using System.Numerics;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Derives the camera, sun and draw groups for the frame and uploads the uniforms every pass shares.</summary>
internal sealed class FrameSetupStage(StageServices services) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        var profile = services.Profile;
        var lighting = frame.Lighting;
        services.Drawer.DefaultBake.Strength = lighting.DefaultBakeStrength;
        var environment = frame.Environment.ResolveLighting(lighting);

        var cam = CameraData.From(frame.Camera, frame.Targets.Width, frame.Targets.Height);
        if (lighting.EdgeFovDegrees > 0f)
            cam = cam with { StatedFovRadians = MathF.Min(float.DegreesToRadians(lighting.EdgeFovDegrees), frame.Camera.FovRadians) };
        var flipped = cam.FlippedY();
        var sunWorld = SunDirection.FromElevationAzimuth(lighting.SunElevation, lighting.SunAzimuth);
        var sunView = SunDirection.ToView(sunWorld, cam.View);
        bool gameOrigin = ClipOrigin.Supported(services.Gl);

        var (casting, groups, opaque) = BuildGroups(frame);
        frame.Setup = new FrameSetup(cam, flipped, gameOrigin, sunWorld, sunView,
            environment.SunColor, environment.HemiSky, environment.HemiGround, casting, groups, opaque);

        services.Resources.Bind(profile.Camera(FrameUniformKeys.SceneCamera, cam));
        services.Resources.Bind(profile.Camera(FrameUniformKeys.GBufferCamera, gameOrigin ? cam : flipped));
        services.Resources.Bind(profile.Lighting(BuildSceneLighting(frame.Setup, frame.Lighting, environment, frame.Targets.Height)));
    }

    // The game's own ratio (ModelSceneExtension::calcParticle_, p_dynamic_resolution_ratio): the view's height over 900 once it is taller than that, else 1. The resolve programs
    // multiply the pixel offsets of their toon edge by it, so the edge keeps its share of the picture as the picture grows.
    const float ReferenceHeight = 900f;

    static SceneLightingData BuildSceneLighting(FrameSetup setup, LightingContext lighting, EnvironmentLighting environment, int renderHeight) =>
        new(setup.SunView, setup.SunWorld, environment.SunColor, environment.HemiSky, environment.HemiGround,
            environment.VolumeMaskColor, environment.VolumeMaskIntensity, RenderTargets.ShadowMapSize,
            lighting.MidScale, lighting.HighlightScale, environment.EntranceSky, environment.EntranceGround,
            renderHeight > ReferenceHeight ? renderHeight / ReferenceHeight : 1f);

    // Each actor gets its own group, with uniforms built fresh from its placement and pose, and its shapes filtered fresh so
    // toggling a shape takes effect on the next frame.
    (List<ActorDrawGroup> Casting, List<ActorDrawGroup> Groups, List<ActorDrawGroup> Opaque) BuildGroups(FrameContext frame)
    {
        var profile = services.Profile;

        var casting = frame.Request.Actors.Select(a => new ActorDrawGroup(
            profile.Actor(new SkinningData(a.ModelMatrixRows, a.Model.Skeleton, a.BoneWorldMatrices)),
            a.ModelMatrixRows,
            a.Model.Shapes.Where(s => s.Enabled && !s.LeftOutOfOrdinaryPasses && (!s.Hidden || s.CastsShadow)).ToList())).ToList();

        foreach (var batch in frame.Instances.Where(b => b.Visible.Count > 0))
            casting.Add(ActorDrawGroup.ForBatch(profile, batch,
                batch.Model.Shapes.Where(s => s.Enabled && !s.LeftOutOfOrdinaryPasses && (!s.Hidden || s.CastsShadow) && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList()));

        List<ActorDrawGroup> groups = [.. casting.Select(g => g with { Shapes = g.Shapes.Where(s => !s.Hidden).ToList() })];
        var opaque = groups
            .Select(g => g with { Shapes = g.Shapes.Where(s => !s.Blend && !s.ReadsSceneColor).ToList() })
            .Where(g => g.Shapes.Count > 0).ToList();
        return (casting, groups, opaque);
    }
}
