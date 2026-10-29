using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Deferred.PassIds;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Effects;
using WildRenderingSharp.Profiles.Totk.Sky.Precompute;
using WildRenderingSharp.Profiles.Totk.Stages;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>
/// Runs the game's own <c>preshading_chara</c> and <c>preshading_field</c>, with their leaf, entrance and water variants, over the pixels the passes they
/// serve light, in place of the approximated sun shadow, fog, misc and light buffers there. They need the shadow cascades, so a frame without them keeps
/// the approximation.
/// </summary>
/// <remarks>
/// They run once the approximated buffers are drawn and before PreShadow is filtered, as the game's own passes do, and they use their own camera,
/// environment and scene-material blocks and their own cascade sampler, so nothing the other passes read changes.
/// </remarks>
internal sealed class GamePreShading(StageServices services, DeferredScene scene, PassIdStamper stamper, CubeEnvironment cubeEnvironment, SkyBake skyBake,
    EntranceInterior interior)
    : IPreShadingOverride, IDisposable
{
    const string CharaPass = "preshading_chara", FieldPass = "preshading_field", LeafPass = "preshading_field_leaf", EntrancePass = "preshading_field_entrance",
        WaterPass = "preshading_field_water", LeafResolvePass = "field_leaf", EntranceResolvePass = "field_entrance", CharaCameraKey = "ctx_preshading",
        FieldCameraKey = "ctx_preshading_field",
        EnvironmentKey = "env_preshading", SceneMaterialKey = "scenemat_preshading", CharaResolvePrefix = "chara_";

    // The field programs read their tiles' flags from a storage buffer at this binding.
    const uint TileFlagsBinding = 0;

    readonly GL _gl = services.Gl;
    readonly PreShadingFramebuffer _charaFramebuffer = new(services.Gl, writesAmbient: false);
    readonly PreShadingFramebuffer _fieldFramebuffer = new(services.Gl, writesAmbient: true);
    readonly PreShadingTextures _textures = new(services.Gl);
    readonly FogExpandPass _fogExpand = new(services.Gl);
    readonly DilatePass _dilate = new(services.Gl);
    readonly ShadowFadePass _shadowFade = new(services.Gl);
    readonly TileFlagsBuffer _tileFlags = new(services.Gl, TileFlagsBuffer.PreShadingWord);
    PreShadingPass? _chara, _field, _leaf, _entrance, _water;
    VolumeMaskPass? _volumeMask;
    bool _loaded, _volumeMaskLoaded;

    // The sky inputs the pre-shading last bound, for the forward programs whose own fog reads the same ones; null before the first frame.
    public ForwardSkyInputs? Sky { get; private set; }

    public unsafe void Run(FrameContext frame)
    {
        // Nothing wrote PreFog but this, so it is black wherever the game's passes do not draw.
        _gl.ClearTexImage(frame.Targets.PreFog.Handle, 0, PixelFormat.Rgba, PixelType.Float, null);
        _gl.ClearTexImage(frame.Targets.PreFogHalf.Handle, 0, PixelFormat.Rgba, PixelType.Float, null);

        if (frame.Shadow.Cascades is not { } cascades || !TryLoad()
            || services.Resources.Held(FrameUniformKeys.Environment) is not { } environment
            || services.Resources.Held(FrameUniformKeys.SceneMaterial) is not { } sceneMaterial)
            return;

        var resources = services.Resources;
        var targets = frame.Targets;
        cubeEnvironment.Set(frame.Setup.HemiSky, frame.Setup.HemiGround);
        var totk = frame.TotkEnvironment();
        var fog = PreShadingFog.From(totk, SkyStage.Intensity(frame), totk.Settings.SkyFogStrength);
        var (entranceSky, entranceGround) = totk.EntranceLight(frame.Setup.HemiSky, frame.Setup.HemiGround);
        var withFog = TotkPreShadingEnvironment.From(environment, EnvironmentKey, fog, entranceSky, entranceGround, totk.DungeonEntrance);
        resources.Bind(withFog);

        // The passes read PreShadow's static-shadow channel, which is 1 where nothing is statically shadowed; the approximation leaves it 0, so they read a copy with it set.
        _gl.CopyImageSubData(targets.PreShadow.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            targets.PreShadowScratch.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)targets.PreShadow.Width, (uint)targets.PreShadow.Height, 1);
        FillStaticShadow(targets, targets.PreShadowScratch);
        uint volumeMask = totk.Settings.UseVolumeMaskEffect && VolumeMask() is { } mask ? mask.Run(resources, targets, frame.Setup).Handle : 0;
        _textures.Bind(targets, targets.PreShadowScratch, cubeEnvironment.Handle, skyBake.BakedInscatter, fog.VolumeMask, volumeMask, interior.Thickness);
        Sky = new ForwardSkyInputs(cubeEnvironment.Handle, skyBake.BakedInscatter, _textures.VolumeMaskHandle);

        _gl.Disable(EnableCap.DepthTest);
        bool culling = _gl.IsEnabled(EnableCap.CullFace);
        _gl.Disable(EnableCap.CullFace);
        ClipOrigin.Game(_gl, true);

        DrawChara(frame, cascades);
        var lit = TotkPreShadingSceneMaterial.From(sceneMaterial, SceneMaterialKey, frame.Setup.HemiSky, frame.Setup.HemiGround);
        DrawField(frame, cascades, lit);
        DrawUntiled(frame, cascades, lit, _leaf, LeafResolvePass);
        // The hall seen through a shrine's doorway is under the ground, out of the world's fog, and only the entrance's own haze is over it.
        resources.Bind(TotkPreShadingEnvironment.From(environment, EnvironmentKey, PreShadingFog.Off(totk), entranceSky, entranceGround, totk.DungeonEntrance));
        DrawUntiled(frame, cascades, lit, _entrance, EntranceResolvePass);
        resources.Bind(withFog);
        DrawUntiled(frame, cascades, lit, _water, DeferredScene.WaterPass);

        ClipOrigin.Game(_gl, false);
        if (culling)
            _gl.Enable(EnableCap.CullFace);

        _fogExpand.Run(resources, targets);
        _shadowFade.Run(resources, targets, frame.Camera.NearPlane, frame.Camera.FarPlane);
        _dilate.Run(resources, targets);
        _textures.Unbind();
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        resources.BindEnvironment();
        resources.BindUbo(FrameUniformKeys.SceneMaterial, TotkBindings.SceneMaterial);
        _gl.ActiveTexture(TextureUnit.Texture0);
        GLDiagnostics.CheckPass(_gl, "game pre-shading");
    }

    public void Dispose()
    {
        _chara?.Dispose();
        _field?.Dispose();
        _leaf?.Dispose();
        _entrance?.Dispose();
        _water?.Dispose();
        _volumeMask?.Dispose();
        _textures.Dispose();
        _fogExpand.Dispose();
        _dilate.Dispose();
        _shadowFade.Dispose();
        _tileFlags.Dispose();
        _charaFramebuffer.Dispose();
        _fieldFramebuffer.Dispose();
    }

    // The character passes' pixels are the ones their shapes stamp.
    void DrawChara(FrameContext frame, ScreenSpaceShadowAndAoPass.CascadeParams cascades)
    {
        HashSet<int> passIds = [.. scene.ResolvedPasses.Where(p => p.Name.StartsWith(CharaResolvePrefix, StringComparison.Ordinal)).Select(p => p.PassIndex + 1)];
        if (_chara is null || passIds.Count == 0)
            return;

        var resources = services.Resources;
        resources.Bind(PreShadingCascades.Camera(CharaCameraKey, frame, cascades, field: false));
        resources.BindUbo(FrameUniformKeys.SceneMaterial, TotkBindings.SceneMaterial);
        _charaFramebuffer.Bind(frame.Targets);
        _chara.Draw(resources, Mask(frame, passIds, claimsUnstamped: false));
    }

    // The field pass lights what its resolve pass does: the pixels stamped with it, and the terrain, which no shape stamps.
    void DrawField(FrameContext frame, ScreenSpaceShadowAndAoPass.CascadeParams cascades, Ubo sceneMaterial)
    {
        int index = scene.PassIndex(DeferredScene.DefaultPass);
        if (_field is null || index < 0 || scene.ResolvedPasses.All(p => p.PassIndex != index))
            return;

        var resources = services.Resources;
        resources.Bind(PreShadingCascades.Camera(FieldCameraKey, frame, cascades, field: true));
        resources.Bind(sceneMaterial);
        _tileFlags.Reset();
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, TileFlagsBinding, _tileFlags.Handle);
        _fieldFramebuffer.Bind(frame.Targets);
        _field.Draw(resources, Mask(frame, new HashSet<int> { index + 1 }, claimsUnstamped: stamper.ClaimPass(frame) == index));
    }

    // The leaf, entrance and water passes light the same way from the same blocks, over the pixels their own resolve passes light, and draw as one quad.
    void DrawUntiled(FrameContext frame, ScreenSpaceShadowAndAoPass.CascadeParams cascades, Ubo sceneMaterial, PreShadingPass? pass, string resolvePass)
    {
        int index = scene.PassIndex(resolvePass);
        if (pass is null || index < 0 || scene.ResolvedPasses.All(p => p.PassIndex != index))
            return;

        var resources = services.Resources;
        resources.Bind(PreShadingCascades.Camera(CharaCameraKey, frame, cascades, field: false));
        resources.Bind(sceneMaterial);
        _fieldFramebuffer.Bind(frame.Targets);
        pass.Draw(resources, Mask(frame, new HashSet<int> { index + 1 }, claimsUnstamped: false));
    }

    static PreShadingMask Mask(FrameContext frame, IReadOnlySet<int> passIds, bool claimsUnstamped) =>
        new(frame.Targets.PassId.Handle, frame.Targets.LinearDepth.Handle, passIds, claimsUnstamped,
            new Vector2(frame.Targets.PreShadow.Width, frame.Targets.PreShadow.Height));

    // Sets the copy's y channel to 1 and leaves the rest.
    void FillStaticShadow(RenderTargets targets, GpuTexture copy)
    {
        targets.BindColorTarget(copy);
        _gl.ColorMask(false, true, false, false);
        _gl.ClearColor(0f, 1f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.ColorMask(true, true, true, true);
    }

    // The game's volume-mask effect, or null when it was not prepared.
    VolumeMaskPass? VolumeMask()
    {
        if (!_volumeMaskLoaded)
        {
            _volumeMask = VolumeMaskPass.Create(_gl, services.Programs, services.Directories);
            _volumeMaskLoaded = true;
        }
        return _volumeMask;
    }

    bool TryLoad()
    {
        if (!_loaded)
        {
            _chara = PreShadingPass.Load(_gl, services.Programs, services.Directories, CharaPass);
            _field = PreShadingPass.Load(_gl, services.Programs, services.Directories, FieldPass);
            _leaf = PreShadingPass.Load(_gl, services.Programs, services.Directories, LeafPass);
            _entrance = PreShadingPass.Load(_gl, services.Programs, services.Directories, EntrancePass);
            _water = PreShadingPass.Load(_gl, services.Programs, services.Directories, WaterPass);
            _loaded = true;
        }
        return _chara is not null || _field is not null || _leaf is not null || _entrance is not null || _water is not null;
    }
}
