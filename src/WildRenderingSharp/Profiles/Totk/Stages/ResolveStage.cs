using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>
/// Lights the G-buffer one deferred pass at a time. Shapes that read the lit scene (glass, water) can only be drawn once it exists,
/// so when any are present the resolve runs in two halves: the scene without them, then those shapes over a copy of it, then their
/// own pixels.
/// </summary>
public sealed class ResolveStage(
    FrameServices services, DeferredScene scene, TerrainRenderer terrain,
    ScreenSpaceLightingStage screenSpaceLighting, PassIdMaskPass passIdMask) : IFrameStage, IDisposable
{
    readonly DeferredResolvePass _resolve = new(services.Gl);
    readonly SceneColorShapePass _sceneColorShapes = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var resources = services.Resources;
        var targets = frame.Targets;

        bool hasSceneColorShapes = SceneColorShapePass.Any(frame.Groups);
        var sceneColorPasses = hasSceneColorShapes
            ? frame.Groups.SelectMany(g => g.Shapes).Where(s => s.ReadsSceneColor).Select(s => s.DeferredPass()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // The host's water reads the lit scene too, and is lit by its own pass.
        var waterHost = frame.TerrainDrawn && frame.TotkEnvironment().Terrain is { HasWater: true } && terrain.Shading.WaterAvailable
            ? frame.TotkEnvironment().Terrain : null;
        if (waterHost is not null)
        {
            scene.EnsurePass(DeferredScene.WaterPass);
            sceneColorPasses.Add(DeferredScene.WaterPass);
        }

        bool twoHalves = hasSceneColorShapes || waterHost is not null;
        _resolve.SetEnvironmentColor(frame.HemiSky);

        // The pass that lights the terrain, which no actor stamps, in whichever half it runs in.
        int defaultPass = frame.TotkEnvironment().Terrain is not null ? scene.PassIndex(DeferredScene.DefaultPass) : -1;

        _resolve.Run(resources, targets, scene.ResolvedPasses, lighting.EmissionScale, lighting.SceneGain, lighting.Exposure,
            twoHalves ? name => !sceneColorPasses.Contains(name) : null, defaultPass);
        GLDiagnostics.CheckPass(services.Gl, "deferred resolve");

        if (twoHalves)
            ResolveSceneColorShapes(frame, hasSceneColorShapes, waterHost, sceneColorPasses, defaultPass);
    }

    void ResolveSceneColorShapes(FrameContext frame, bool hasShapes, ITerrainHost? waterHost, HashSet<string> sceneColorPasses, int defaultPass)
    {
        var lighting = frame.Lighting;
        var resources = services.Resources;
        var targets = frame.Targets;
        var gl = services.Gl;

        float emissionUnits = lighting.Exposure / MathF.Max(1e-4f, lighting.EmissionScale);
        _sceneColorShapes.CopyInputs(resources, targets, emissionUnits);
        if (hasShapes)
        {
            resources.BindCamera(FrameUniformKeys.GBufferCamera);
            ClipOrigin.Game(gl, true);
            _sceneColorShapes.Run(resources, targets, frame.Groups, services.Programs);
            ClipOrigin.Game(gl, false);
        }
        if (waterHost is not null)
            terrain.DrawWater(waterHost, targets, frame.Camera, stamp: false);
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        GLDiagnostics.CheckPass(gl, "scene-colour shapes");

        // The G-buffer under those pixels just changed, so the screen-space inputs are derived again.
        screenSpaceLighting.Repeat(frame);
        passIdMask.Run(resources, targets, frame.Groups, scene.PassNames, frame.MaskViewProj, frame.Camera.NearPlane, frame.Camera.FarPlane);
        if (waterHost is not null)
        {
            terrain.DrawWater(waterHost, targets, frame.Camera, stamp: true);
            resources.BindCamera(FrameUniformKeys.SceneCamera);
        }
        _resolve.Run(resources, targets, scene.ResolvedPasses, lighting.EmissionScale, lighting.SceneGain, lighting.Exposure,
            sceneColorPasses.Contains, defaultPass);
        GLDiagnostics.CheckPass(gl, "scene-colour shapes resolve");
    }

    public void Dispose()
    {
        _resolve.Dispose();
        _sceneColorShapes.Dispose();
    }
}
