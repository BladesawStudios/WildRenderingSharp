using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Deferred.PassIds;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Lights the G-buffer one deferred pass at a time.</summary>
internal sealed class ResolveStage(
    StageServices services, DeferredScene scene, TerrainRenderer terrain,
    ScreenSpaceLightingStage screenSpaceLighting, PassIdStamper stamper, CubeEnvironment cubeEnvironment, EntranceInterior interior) : IFrameStage, IDisposable
{
    readonly DeferredResolvePass _resolve = new(services.Gl, cubeEnvironment);
    readonly SceneColorShapePass _sceneColorShapes = new(services.Gl);

    (string Pass, string Path)? _pendingTrace;

    public int DebugPass { get => _resolve.DebugPass; set => _resolve.DebugPass = value; }

    public void RequestTrace(string pass, string path) => _pendingTrace = (pass, path);

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var resources = services.Resources;
        var targets = frame.Targets;
        StartPendingTrace(targets);
        cubeEnvironment.Set(frame.Setup.HemiSky, frame.Setup.HemiGround);

        bool hasSceneColorShapes = SceneColorShapePass.Any(frame.Setup.Groups);
        var sceneColorPasses = hasSceneColorShapes
            ? frame.Setup.Groups.SelectMany(g => g.Shapes).Where(s => s.ReadsSceneColor).Select(s => s.DeferredPass()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // The host's water reads the lit scene too, and is lit by its own pass.
        var waterHost = terrain.GBufferDrawn && frame.TotkEnvironment().Terrain is { HasWater: true } && terrain.Shading.WaterAvailable
            ? frame.TotkEnvironment().Terrain : null;
        if (waterHost is not null)
        {
            scene.EnsurePass(DeferredScene.WaterPass);
            sceneColorPasses.Add(DeferredScene.WaterPass);
        }

        // The see-through shapes of the hall a shrine's doorway shows; drawn over it once it is lit, the entrance's pass is lit again in the second half.
        var doorway = interior.Portal != 0
            ? PlacedShapes.Of(frame, services.Profile, s => s.IsSeenThroughDoorway() && s.ReadsSceneColor)
            : [];

        bool twoHalves = hasSceneColorShapes || waterHost is not null || doorway.Count > 0;

        // The pass that lights the terrain, which no actor stamps, in whichever half it runs in.
        int defaultPass = stamper.ClaimPass(frame);

        _resolve.Run(resources, targets, scene.ResolvedPasses, lighting.EmissionScale, lighting.SceneGain, lighting.Exposure,
            twoHalves ? name => !sceneColorPasses.Contains(name) : null, defaultPass);
        GLDiagnostics.CheckPass(services.Gl, "deferred resolve");

        if (twoHalves)
            ResolveSceneColorShapes(frame, hasSceneColorShapes, waterHost, sceneColorPasses, doorway, defaultPass);
    }

    void StartPendingTrace(RenderTargets targets)
    {
        if (_pendingTrace is not var (pass, path))
            return;
        _pendingTrace = null;
        string header = string.Join(Environment.NewLine, $"pass {pass}", $"renderer {services.Gl.GetStringS(StringName.Renderer)}", $"size {targets.Width}x{targets.Height}");
        _resolve.Trace(pass, p => ResolveTrace.Begin(services.Gl, services.Programs, p.Source, path, header, targets.Width / 2, targets.Height / 2));
    }

    void ResolveSceneColorShapes(FrameContext frame, bool hasShapes, ITerrainHost? waterHost, HashSet<string> sceneColorPasses,
        List<ActorDrawGroup> doorway, int defaultPass)
    {
        var lighting = frame.Lighting;
        var resources = services.Resources;
        var targets = frame.Targets;
        var gl = services.Gl;

        float emissionUnits = lighting.Exposure / MathF.Max(1e-4f, lighting.EmissionScale);
        _sceneColorShapes.CopyInputs(resources, targets, emissionUnits);
        if (hasShapes || doorway.Count > 0)
        {
            resources.BindCamera(FrameUniformKeys.GBufferCamera);
            ClipOrigin.Game(gl, true);
            if (hasShapes)
                _sceneColorShapes.Run(resources, targets, frame.Setup.Groups, services.Drawer);
            if (doorway.Count > 0)
            {
                interior.ThroughDoorway(resources, targets, () => _sceneColorShapes.Run(resources, targets, doorway, services.Drawer));
                sceneColorPasses = [.. sceneColorPasses, LoadedShapeExtensions.EntrancePass];
            }
            ClipOrigin.Game(gl, false);
        }
        if (waterHost is not null)
            terrain.DrawWater(waterHost, targets, frame.Camera, stamp: false);
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        GLDiagnostics.CheckPass(gl, "scene-colour shapes");

        // The G-buffer under those pixels just changed, so the mask and the screen-space inputs are derived again.
        stamper.Run(resources, frame);
        screenSpaceLighting.Repeat(frame);
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
