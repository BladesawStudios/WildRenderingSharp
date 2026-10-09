using System.Numerics;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Draws the sun's shadow map, or its cascades.</summary>
internal sealed class ShadowStage(StageServices services, TerrainRenderer terrain) : IFrameStage
{
    readonly ShadowPass _shadow = new(services.Gl);

    GLResourceCache Resources => services.Resources;

    public void Run(FrameContext frame)
    {
        var request = frame.Request;
        var cache = frame.ShadowCache;

        if (request.ShadowFocus is { } shadowFocus)
            foreach (var batch in frame.Instances)
                batch.Shadow.Update(shadowFocus);

        var cascadeParams = request.ShadowCascades is { Count: > 0 } cascades && frame.ShadowMapOverride is null
            ? RenderCascades(frame, cascades)
            : null;

        var modelRowsPerActor = request.Actors.Select(a => a.ModelMatrixRows).ToArray();
        bool reuse = cascadeParams is not null
            || frame.ShadowMapOverride is null && cache.ModelRowsPerActor is not null && cache.SunWorld == frame.Setup.SunWorld
                && ShadowSignatures.ActorRowsEqual(cache.ModelRowsPerActor, modelRowsPerActor)
                && ShadowSignatures.PosesEqual(cache.BonesPerActor, request.Actors)
                && cache.Focus == request.ShadowFocus
                && cache.InstanceSignature == ShadowSignatures.InstanceSignature(frame.Instances, request.ShadowFocus is not null);

        if (cascadeParams is not null)
        {
            float radius = cache.CascadeRadius[0];
            var center = request.ShadowCascades![0].Center;
            frame.Shadow = new ShadowResult(cache.CascadeLight[0], center - new Vector3(radius), center + new Vector3(radius), cascadeParams);
        }
        else if (reuse)
        {
            frame.Shadow = new ShadowResult(cache.LightMatrices, cache.RotatedLo, cache.RotatedHi);
        }
        else
        {
            DrawShadowMap(frame, modelRowsPerActor);
        }
    }

    void DrawShadowMap(FrameContext frame, Vector4[][] modelRowsPerActor)
    {
        var request = frame.Request;
        var cache = frame.ShadowCache;

        (Vector3 lo, Vector3 hi) = request.ShadowFocus is { } focus
            ? (focus.Center - new Vector3(focus.Radius), focus.Center + new Vector3(focus.Radius))
            : ShadowSignatures.CombinedBounds(request.Actors, frame.Instances);
        var light = ShadowPass.BuildLightMatrices(lo, hi, frame.Setup.SunWorld);
        frame.Shadow = new ShadowResult(light, lo, hi);

        if (frame.ShadowMapOverride is not null)
            return;

        Resources.Bind(services.Profile.Camera(FrameUniformKeys.LightCamera, frame.Setup.Cam.ForLight(light.View, light.Proj)));
        _shadow.Run(Resources, frame.Targets, ShadowGroups(frame, request.ShadowFocus), services.Drawer);
        GLDiagnostics.CheckPass(services.Gl, "shadow pass");
        Resources.BindCamera(FrameUniformKeys.SceneCamera);

        cache.SunWorld = frame.Setup.SunWorld;
        cache.ModelRowsPerActor = modelRowsPerActor;
        cache.BonesPerActor = request.Actors.Select(a => (Matrix4x4[]?)a.BoneWorldMatrices?.Clone()).ToArray();
        cache.Focus = request.ShadowFocus;
        cache.InstanceSignature = ShadowSignatures.InstanceSignature(frame.Instances, request.ShadowFocus is not null);
        cache.RotatedLo = lo;
        cache.RotatedHi = hi;
        cache.LightMatrices = light;
    }

    // What the single shadow map draws. With a focus, batches cast from their own shadow runs, including a batch the camera
    // sees none of, instead of from what is on screen.
    List<ActorDrawGroup> ShadowGroups(FrameContext frame, ShadowFocus? focus)
    {
        if (focus is null)
            return [.. frame.Setup.CastingGroups.Select(WithoutSceneColorShapes)];

        var groups = frame.Setup.CastingGroups.Where(g => g.Batch is null).Select(WithoutSceneColorShapes).ToList();
        foreach (var batch in frame.Instances.Where(b => b.Shadow.Visible.Count > 0))
            groups.Add(ActorDrawGroup.ForBatch(services.Profile, batch, CastingShapes(batch), shadowRuns: true));
        return groups;
    }

    ScreenSpaceShadowAndAoPass.CascadeParams RenderCascades(FrameContext frame, IReadOnlyList<ShadowFocus> cascades)
    {
        var request = frame.Request;
        var cache = frame.ShadowCache;
        var targets = frame.Targets;
        int count = Math.Min(cascades.Count, RenderTargets.MaxCascades);
        var viewProj = new Matrix4x4[count];
        var texelWorld = new float[count];
        var bias = new float[count];
        long actorSignature = ShadowSignatures.ActorSignature(request.Actors);
        bool drew = false;

        for (int c = 0; c < count; c++)
        {
            var focus = cascades[c];
            var lo = focus.Center - new Vector3(focus.Radius);
            var hi = focus.Center + new Vector3(focus.Radius);
            float lightRadius = (hi - lo).Length() * 0.5f + 1e-4f;
            var light = ShadowPass.BuildLightMatrices(lo, hi, frame.Setup.SunWorld);
            var right = new Vector3(light.View.M11, light.View.M21, light.View.M31);
            var up = new Vector3(light.View.M12, light.View.M22, light.View.M32);
            foreach (var batch in frame.Instances)
                batch.Shadow.UpdateCascade(c, focus, right, up, lightRadius);

            long signature = CascadeSignature(frame, c, focus, actorSignature);
            if (cache.CascadeSignature[c] != signature)
            {
                DrawCascade(frame, c, focus, light);
                cache.CascadeSignature[c] = signature;
                cache.CascadeLight[c] = light;
                drew = true;
            }
            cache.CascadeRadius[c] = focus.Radius;
            viewProj[c] = cache.CascadeLight[c].ViewProj;
            texelWorld[c] = 2f * lightRadius / RenderTargets.CascadeSize;
            bias[c] = request.ShadowBias / (lightRadius * 5f - 0.01f);
        }

        if (drew)
        {
            GLDiagnostics.CheckPass(services.Gl, "shadow cascades");
            frame.Stats.Shadow = services.Drawer.TakeCounts();
            Resources.BindCamera(FrameUniformKeys.SceneCamera);
        }
        return new ScreenSpaceShadowAndAoPass.CascadeParams(targets.ShadowCascades.Handle, viewProj, texelWorld, bias);
    }

    static long CascadeSignature(FrameContext frame, int cascade, ShadowFocus focus, long actorSignature)
    {
        var hash = new HashCode();
        hash.Add(frame.Setup.SunWorld);
        hash.Add(focus);
        hash.Add(actorSignature);
        hash.Add(frame.TotkEnvironment().Terrain?.ShadowVersion ?? 0);
        hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frame.Targets));
        foreach (var batch in frame.Instances)
        {
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(batch));
            foreach (var run in batch.Shadow.Cascade(cascade))
                hash.Add(run);
        }
        return (uint)hash.ToHashCode() | (1L << 40);
    }

    void DrawCascade(FrameContext frame, int cascade, ShadowFocus focus, ShadowPass.LightMatrices light)
    {
        Resources.Bind(services.Profile.Camera(FrameUniformKeys.LightCamera, frame.Setup.Cam.ForLight(light.View, light.Proj)));

        var groups = frame.Setup.CastingGroups.Where(g => g.Batch is null).Select(WithoutSceneColorShapes).ToList();
        foreach (var batch in frame.Instances.Where(b => b.Shadow.Cascade(cascade).Count > 0))
            groups.Add(ActorDrawGroup.ForBatch(services.Profile, batch, CastingShapes(batch), shadowRuns: true, cascade: cascade));
        _shadow.Run(Resources, frame.Targets, groups, services.Drawer, cascade);

        if (frame.TotkEnvironment().Terrain is { } host && terrain.Available)
            terrain.DrawShadow(host, cascade, frame.Camera, focus, light, frame.Setup.Cam);
    }

    static ActorDrawGroup WithoutSceneColorShapes(ActorDrawGroup group) =>
        group with { Shapes = group.Shapes.Where(s => !s.ReadsSceneColor).ToList() };

    static List<LoadedShape> CastingShapes(InstanceBatch batch) =>
        batch.Model.Shapes.Where(s => s.Enabled && s.CastsShadow && !s.ReadsSceneColor && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList();
}
