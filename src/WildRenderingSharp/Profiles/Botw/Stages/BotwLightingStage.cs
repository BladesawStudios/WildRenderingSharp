using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>
/// The game's lighting before the shading passes: the sun's shadow cascade, then its own pre-shading passes for characters, which turn
/// the G-buffer into the shadow, light and fog buffers the shading passes composite.
/// </summary>
public sealed class BotwLightingStage(StageServices services, BotwPasses passes) : IFrameStage, IDisposable
{
    const string ContextKey = "ctx_botw_preshading";
    const int ShadowAttachment = 5, FogAttachment = 1, DiffuseAttachment = 3;
    const int CharacterIdLow = 8, CharacterIdHigh = 15;

    readonly ShadowPass _shadow = new(services.Gl);
    readonly LinearDepthPass _linearDepth = new(services.Gl);
    readonly PreShadingTargets _targets = new(services.Gl);
    BotwPass? _shadowPass, _lightPass;
    bool _loaded;
    uint _cube;
    (Vector3 Sky, Vector3 Ground) _cubeColors;

    public PreShadingTargets PreShading => _targets;

    public int Shadow => ShadowAttachment;
    public int Fog => FogAttachment;
    public int Diffuse => DiffuseAttachment;

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;
        var targets = frame.Targets;
        if (!_loaded)
        {
            _shadowPass = passes.Load("preshading_shadow_chara", CharacterIdLow, CharacterIdHigh);
            _lightPass = passes.Load("preshading_chara", CharacterIdLow, CharacterIdHigh);
            _loaded = true;
        }
        _targets.Ensure(targets.Width, targets.Height);

        _linearDepth.Run(services.Resources, targets, frame.Camera.NearPlane, frame.Camera.FarPlane);
        UploadConstants(frame, DrawCascade(frame));
        passes.EnsureLightAnalyzed(frame.HemiSky, frame.HemiGround);

        _targets.Clear(ShadowAttachment, 1, 1, 1, 1);
        _targets.Clear(DiffuseAttachment, 0, 0, 0, 0);
        _targets.Clear(FogAttachment, 0, 0, 0, 0);

        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        services.Resources.BindUbo(FrameUniformKeys.SceneMaterial, BotwBindings.SceneMaterial);
        ClipOrigin.Game(gl, true);
        DrawShadowPass(targets);
        DrawLightPass(frame);
        ClipOrigin.Game(gl, false);
        GLDiagnostics.CheckPass(gl, "BotW pre-shading");

        _targets.CopyToArray(DiffuseAttachment);
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    // The environment and context blocks the pre-shading passes read; the context carries the cascade's view-to-texture matrix.
    void UploadConstants(FrameContext frame, Matrix4x4 viewToShadow)
    {
        float texel = 1f / RenderTargets.CascadeSize;
        services.Resources.Bind(BotwUniforms.Environment(frame.SunView, frame.SunColor, frame.HemiSky, frame.HemiGround, frame.Cam.ViewInv, texel));
        services.Resources.Bind(BotwUniforms.CascadeCamera(ContextKey, frame.Cam, viewToShadow, 1e9f, 1e9f));
    }

    void DrawShadowPass(RenderTargets targets)
    {
        if (_shadowPass is null)
            return;
        _targets.Bind(ShadowAttachment);
        passes.BindAt(BotwSamplers.ShadowChara.Albedo, targets.GBuffer[BotwGBuffer.Albedo].Handle);
        passes.BindAt(BotwSamplers.ShadowChara.Normal, targets.GBuffer[BotwGBuffer.Normal].Handle);
        passes.BindAt(BotwSamplers.ShadowChara.WorldShadow, passes.Black);
        passes.BindAt(BotwSamplers.ShadowChara.ShadowCascade, targets.ShadowCascades.Handle, TextureTarget.Texture2DArray);
        passes.BindAt(BotwSamplers.ShadowChara.HalfDepth, targets.LinearDepthHalf.Handle);
        passes.BindAt(BotwSamplers.ShadowChara.ResourceTexture1, passes.White);
        BindMaskInputs(targets);
        passes.Draw(_shadowPass);
    }

    void DrawLightPass(FrameContext frame)
    {
        if (_lightPass is null)
            return;
        var targets = frame.Targets;
        _targets.Bind(0, 1, 2, 3, 4);
        EnsureCube(frame.HemiSky, frame.HemiGround);
        passes.BindAt(BotwSamplers.LightChara.Albedo, targets.GBuffer[BotwGBuffer.Albedo].Handle);
        passes.BindAt(BotwSamplers.LightChara.Normal, targets.GBuffer[BotwGBuffer.Normal].Handle);
        passes.BindAt(BotwSamplers.LightChara.Emission, targets.GBuffer[BotwGBuffer.Emission].Handle);
        passes.BindAt(BotwSamplers.LightChara.Projection, passes.White);
        passes.BindAt(BotwSamplers.LightChara.MaterialId, targets.GBuffer[BotwGBuffer.MaterialId].Handle);
        passes.BindAt(BotwSamplers.LightChara.SkyInscatter, passes.White);
        passes.BindAt(BotwSamplers.LightChara.Expand, passes.White);
        passes.BindAt(BotwSamplers.LightChara.VolumeMask, passes.Black);
        passes.BindAt(BotwSamplers.LightChara.HalfDepth, targets.LinearDepthHalf.Handle);
        passes.BindAt(BotwSamplers.LightChara.ResourceTexture0, passes.Black);
        passes.BindAt(BotwSamplers.LightChara.CubeEnvironment, _cube, TextureTarget.TextureCubeMap);
        BindMaskInputs(targets);
        passes.Draw(_lightPass);
    }

    void BindMaskInputs(RenderTargets targets)
    {
        passes.BindAt(BotwSamplers.IdTexture, targets.GBuffer[BotwGBuffer.MaterialId].Handle);
        passes.BindAt(BotwSamplers.LightAnalyzed, passes.LightAnalyzed);
    }

    // Draws the one shadow cascade, and returns the matrix from view space to its texture space the shading passes read.
    Matrix4x4 DrawCascade(FrameContext frame)
    {
        var resources = services.Resources;
        var request = frame.Request;
        var (lo, hi) = ShadowSignatures.CombinedBounds(request.Actors, frame.Instances);
        var light = ShadowPass.BuildLightMatrices(lo, hi, frame.SunWorld);
        frame.LightMatrices = light;
        frame.ShadowBoundsLo = lo;
        frame.ShadowBoundsHi = hi;

        resources.Bind(services.Profile.Camera(FrameUniformKeys.LightCamera, frame.Cam.ForLight(light.View, light.Proj)));
        _shadow.Run(resources, frame.Targets, frame.CastingGroups, services.Programs, cascade: 0);
        GLDiagnostics.CheckPass(services.Gl, "shadow cascade");
        resources.BindCamera(FrameUniformKeys.SceneCamera);

        // Clip space [-1, 1] to texture space [0, 1].
        var toTexture = Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f);
        return frame.Cam.ViewInv * light.ViewProj * toTexture;
    }

    // Sky above the horizon and ground below it, which the character pass reflects at a coarse level.
    unsafe void EnsureCube(Vector3 sky, Vector3 ground)
    {
        if (_cube != 0 && _cubeColors == (sky, ground))
            return;
        _cubeColors = (sky, ground);
        var gl = services.Gl;
        if (_cube == 0)
            _cube = gl.GenTexture();

        const int Size = 16;
        gl.BindTexture(TextureTarget.TextureCubeMap, _cube);
        for (int face = 0; face < 6; face++)
        {
            Vector3 color = face switch { 2 => sky, 3 => ground, _ => (sky + ground) * 0.5f };
            var texels = new float[Size * Size * 4];
            for (int i = 0; i < Size * Size; i++)
            {
                texels[i * 4] = color.X;
                texels[i * 4 + 1] = color.Y;
                texels[i * 4 + 2] = color.Z;
                texels[i * 4 + 3] = 1f;
            }
            fixed (float* data = texels)
                gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, InternalFormat.Rgba16f, Size, Size, 0, PixelFormat.Rgba, PixelType.Float, data);
        }
        gl.GenerateMipmap(TextureTarget.TextureCubeMap);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
    }

    public void Dispose()
    {
        if (_cube != 0)
            services.Gl.DeleteTexture(_cube);
        _targets.Dispose();
        _linearDepth.Dispose();
    }
}
