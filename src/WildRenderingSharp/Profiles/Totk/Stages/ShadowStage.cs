using System.Numerics;
using Silk.NET.OpenGL;
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
    // The bias a cascade is given at least, in its own texels: the 0.0015 m an actor scene uses over its 0.00125 m texels.
    const float CascadeBiasTexels = 1.2f;

    // The constant part of the cascades' polygon offset, in the depth buffer's own units, which goes with the slope-scaled part the settings hold.
    const float ConstantOffset = 2f;

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

        if (cascadeParams is not null)
        {
            var first = new ShadowFocus(request.ShadowCascades![0].Center, cache.CascadeRadius[0]);
            frame.Shadow = new ShadowResult(cache.CascadeLight[0], first.Min, first.Max, cascadeParams);
        }
        else if (frame.ShadowMapOverride is null && CachedMapIsCurrent(frame, modelRowsPerActor))
        {
            frame.Shadow = new ShadowResult(cache.LightMatrices, cache.RotatedLo, cache.RotatedHi);
        }
        else
        {
            DrawShadowMap(frame, modelRowsPerActor);
        }
    }

    // True when nothing the last shadow map was drawn from has changed.
    static bool CachedMapIsCurrent(FrameContext frame, Vector4[][] modelRowsPerActor)
    {
        var request = frame.Request;
        var cache = frame.ShadowCache;
        return cache.ModelRowsPerActor is not null && cache.SunWorld == frame.Setup.SunWorld
            && ShadowSignatures.ActorRowsEqual(cache.ModelRowsPerActor, modelRowsPerActor)
            && ShadowSignatures.PosesEqual(cache.BonesPerActor, request.Actors)
            && cache.Focus == request.ShadowFocus
            && cache.InstanceSignature == ShadowSignatures.InstanceSignature(frame.Instances, request.ShadowFocus is not null);
    }

    void DrawShadowMap(FrameContext frame, Vector4[][] modelRowsPerActor)
    {
        var request = frame.Request;
        var cache = frame.ShadowCache;

        (Vector3 lo, Vector3 hi) = request.ShadowFocus is { } focus
            ? (focus.Min, focus.Max)
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

        var groups = ActorGroups(frame);
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
        var invRange = new float[count];
        bool drew = false;
        cache.FrameCount++;

        // New targets hold no cascades, so every one of them has to be drawn.
        int targetsId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(targets);
        if (cache.TargetsId != targetsId)
        {
            Array.Clear(cache.CascadeSignature);
            Array.Clear(cache.CascadeHasActors);
            cache.TargetsId = targetsId;
        }

        // A cascade that has changed is drawn at most one a frame, the one waiting longest, and one never drawn at all always is: redrawing every changed cascade at
        // once costs tens of milliseconds as the camera moves, while a cascade left a frame or two behind is still a consistent map under its own light matrices.
        var lights = new ShadowPass.LightMatrices[count];
        var signatures = new long[count];
        var suns = new Vector3[count];
        var parts = new (int Sun, int Focus, int Terrain, int Runs)[count];
        int oldest = -1;
        for (int c = 0; c < count; c++)
        {
            var focus = cascades[c];
            var (lo, hi) = (focus.Min, focus.Max);
            suns[c] = HeldSun(cache, c, frame.Setup.SunWorld);
            lights[c] = ShadowPass.BuildLightMatrices(lo, hi, suns[c]);
            var light = lights[c];
            var right = new Vector3(light.View.M11, light.View.M21, light.View.M31);
            var up = new Vector3(light.View.M12, light.View.M22, light.View.M32);
            foreach (var batch in frame.Instances)
                batch.Shadow.UpdateCascade(c, focus, right, up, (hi - lo).Length() * 0.5f + 1e-4f);

            parts[c] = SignatureParts(frame, c, focus, suns[c]);
            signatures[c] = Combine(parts[c]);
            if (cache.CascadeSignature[c] != signatures[c] && cache.CascadeSignature[c] != 0 && (oldest < 0 || cache.CascadeDrawnAt[c] < cache.CascadeDrawnAt[oldest]))
                oldest = c;
        }

        // What moves is drawn over a copy of the cascade's unmoving casters every frame, so it never changes what the cascade keeps and a creature walking about does
        // not make every cascade redraw its whole map.
        var movers = ActorGroups(frame);
        bool hasMovers = movers.Any(g => g.Shapes.Count > 0);

        for (int c = 0; c < count; c++)
        {
            var focus = cascades[c];
            var (lo, hi) = (focus.Min, focus.Max);
            float lightRadius = (hi - lo).Length() * 0.5f + 1e-4f;
            bool redrawn = cache.CascadeSignature[c] == 0 || c == oldest;
            if (redrawn)
            {
                if (cache.CascadeSignature[c] != 0)
                {
                    var was = cache.CascadeParts[c];
                    if (was.Sun != parts[c].Sun) cache.CascadeCauses[0]++;
                    if (was.Focus != parts[c].Focus) cache.CascadeCauses[1]++;
                    if (was.Terrain != parts[c].Terrain) cache.CascadeCauses[2]++;
                    if (was.Runs != parts[c].Runs) cache.CascadeCauses[3]++;
                }
                cache.CascadeParts[c] = parts[c];
                cache.CascadeSun[c] = suns[c];
                DrawCascade(frame, c, focus, lights[c]);
                cache.CascadeSignature[c] = signatures[c];
                cache.CascadeLight[c] = lights[c];
                cache.CascadeDrawnAt[c] = cache.FrameCount;
                cache.CascadeRedraws[c]++;
                drew = true;
            }
            if (redrawn || hasMovers || cache.CascadeHasActors[c])
            {
                targets.CopyStaticShadowCascade(c);
                if (hasMovers)
                    DrawMovers(frame, c, cache.CascadeLight[c], movers);
                cache.CascadeHasActors[c] = hasMovers;
                drew = true;
            }
            cache.CascadeRadius[c] = focus.Radius;
            viewProj[c] = cache.CascadeLight[c].ViewProj;
            texelWorld[c] = 2f * lightRadius / RenderTargets.CascadeSize;

            // The request's bias is a fixed distance, which suits the finest cascade only: a cascade four times wider has texels
            // four times larger, and a surface within a texel's reach of its own stored depth then shadows itself wherever the
            // bias is smaller than that. So no cascade is given less than the bias a character-sized scene is tuned to, which is
            // a little over one texel.
            float biasMetres = MathF.Max(request.ShadowBias, CascadeBiasTexels * texelWorld[c]);
            bias[c] = biasMetres / (lightRadius * 5f - 0.01f);
            invRange[c] = 1f / (lightRadius * 5f - 0.01f);
        }

        if (drew)
        {
            GLDiagnostics.CheckPass(services.Gl, "shadow cascades");
            frame.Stats.Shadow = services.Drawer.TakeCounts();
            Resources.BindCamera(FrameUniformKeys.SceneCamera);
        }
        return new ScreenSpaceShadowAndAoPass.CascadeParams(targets.ShadowCascades.Handle, viewProj, texelWorld, bias, invRange);
    }

    // How far the sun may move, in degrees, before a cascade is drawn again under the new direction: a cascade's texels are large for the height of what casts into
    // them, so a sun a fraction of a degree off puts a shadow well inside a texel of where it should be, and the finer the cascade the less it can take.
    static readonly float[] SunToleranceDegrees = [0.1f, 0.2f, 0.5f, 1f];

    // The sun the cascade is drawn under: the one it was last drawn under, until the real one has moved past the cascade's tolerance.
    static Vector3 HeldSun(ShadowCache cache, int cascade, Vector3 sun)
    {
        Vector3 held = cache.CascadeSun[cascade];
        if (held == default)
            return sun;
        float cosine = Vector3.Dot(Vector3.Normalize(held), Vector3.Normalize(sun));
        return cosine < MathF.Cos(SunToleranceDegrees[Math.Min(cascade, SunToleranceDegrees.Length - 1)] * MathF.PI / 180f) ? sun : held;
    }

    static (int Sun, int Focus, int Terrain, int Runs) SignatureParts(FrameContext frame, int cascade, ShadowFocus focus, Vector3 sun)
    {
        var runs = new HashCode();
        runs.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frame.Targets));
        foreach (var batch in frame.Instances)
        {
            runs.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(batch));
            foreach (var run in batch.Shadow.Cascade(cascade))
                runs.Add(run);
        }
        return (sun.GetHashCode(), focus.GetHashCode(), (frame.TotkEnvironment().Terrain?.ShadowVersionNear(focus.Center, focus.Radius) ?? 0).GetHashCode(), runs.ToHashCode());
    }

    static long Combine((int Sun, int Focus, int Terrain, int Runs) parts) =>
        (uint)HashCode.Combine(parts.Sun, parts.Focus, parts.Terrain, parts.Runs) | (1L << 40);

    void DrawCascade(FrameContext frame, int cascade, ShadowFocus focus, ShadowPass.LightMatrices light)
    {
        Resources.Bind(services.Profile.Camera(FrameUniformKeys.LightCamera, frame.Setup.Cam.ForLight(light.View, light.Proj)));

        var groups = new List<ActorDrawGroup>();
        foreach (var batch in frame.Instances.Where(b => b.Shadow.Cascade(cascade).Count > 0))
            groups.Add(ActorDrawGroup.ForBatch(services.Profile, batch, CastingShapes(batch), shadowRuns: true, cascade: cascade));

        // Depth is clamped, not clipped, so a caster nearer the sun than the cascade's near plane still shadows it: with a low sun the
        // mountain that shades a valley is far toward the sun, and a cascade can only reach so far.
        var gl = services.Gl;
        gl.Enable(EnableCap.DepthClamp);

        // The game renders its cascades with a depth offset that grows with the slope of the surface to the light, which a flat bias cannot stand in for: a
        // surface the sun meets at a graze changes depth by many texels across one, and without the offset it shadows itself in thin dark lines along the terminator.
        // TODO: the game's own offset for its cascades is not known, so the slope-scaled part is a setting, left at zero until a value is fitted.
        float slope = frame.TotkEnvironment().Settings.ShadowSlopeOffset;
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(slope, slope > 0f ? ConstantOffset : 0f);
        _shadow.Run(Resources, frame.Targets, groups, services.Drawer, cascade, statics: true);

        if (frame.TotkEnvironment().Terrain is { } host && terrain.Available)
            terrain.DrawShadow(host, cascade, frame.Camera, focus, light, frame.Setup.Cam);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.Disable(EnableCap.DepthClamp);
    }

    // The placed actors that move, drawn into a cascade over the copy of what it keeps, under the light matrices that copy was drawn with.
    void DrawMovers(FrameContext frame, int cascade, ShadowPass.LightMatrices light, List<ActorDrawGroup> movers)
    {
        Resources.Bind(services.Profile.Camera(FrameUniformKeys.LightCamera, frame.Setup.Cam.ForLight(light.View, light.Proj)));
        var gl = services.Gl;
        gl.Enable(EnableCap.DepthClamp);
        float slope = frame.TotkEnvironment().Settings.ShadowSlopeOffset;
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(slope, slope > 0f ? ConstantOffset : 0f);
        _shadow.Run(Resources, frame.Targets, movers, services.Drawer, cascade, clear: false);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.Disable(EnableCap.DepthClamp);
    }

    // The placed actors' casters, without the shapes that read the scene colour.
    static List<ActorDrawGroup> ActorGroups(FrameContext frame) =>
        frame.Setup.CastingGroups.Where(g => g.Batch is null).Select(WithoutSceneColorShapes).ToList();

    static ActorDrawGroup WithoutSceneColorShapes(ActorDrawGroup group) =>
        group with { Shapes = group.Shapes.Where(s => !s.ReadsSceneColor).ToList() };

    static List<LoadedShape> CastingShapes(InstanceBatch batch) =>
        batch.Model.Shapes.Where(s => s.Enabled && s.CastsShadow && !s.ReadsSceneColor && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList();
}
