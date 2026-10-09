using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Botw.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>
/// The game's lighting before the shading passes: the sun's shadow cascade, then its own pre-shading passes for characters, which turn
/// the G-buffer into the shadow, light and fog buffers the shading passes composite.
/// </summary>
public sealed class BotwLightingStage(FrameServices services, BotwPasses passes) : IFrameStage, IDisposable
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
        var resources = services.Resources;
        var targets = frame.Targets;
        var camera = frame.Camera;
        var cam = frame.Cam;
        var world = services.Profile.World;

        if (!_loaded)
        {
            _shadowPass = passes.Load("preshading_shadow_chara", CharacterIdLow, CharacterIdHigh);
            _lightPass = passes.Load("preshading_chara", CharacterIdLow, CharacterIdHigh);
            _loaded = true;
        }
        _targets.Ensure(targets.Width, targets.Height);

        _linearDepth.Run(resources, targets, camera.NearPlane, camera.FarPlane);
        var viewToShadow = DrawCascade(frame);

        float texel = 1f / RenderTargets.CascadeSize;
        resources.Ubo(FrameUniformKeys.Environment,
            BotwEnvUbo.From(frame.SunView, frame.SunColor, frame.HemiSky, frame.HemiGround, world.InverseRows(CameraData.Rows(cam.ViewInv, 3)), texel).ToByteArray(),
            BotwBindings.Environment);
        resources.Ubo(ContextKey, BotwUniforms.Context(world, cam).WithCascade(viewToShadow, 1e9f, 1e9f).ToByteArray(), BotwBindings.Camera);
        passes.EnsureLightAnalyzed(frame.HemiSky, frame.HemiGround);

        _targets.Clear(ShadowAttachment, 1, 1, 1, 1);
        _targets.Clear(DiffuseAttachment, 0, 0, 0, 0);
        _targets.Clear(FogAttachment, 0, 0, 0, 0);

        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        resources.BindUbo(FrameUniformKeys.SceneMaterial, BotwBindings.SceneMaterial);
        ClipOrigin.Game(gl, true);

        if (_shadowPass is not null)
        {
            _targets.Bind(ShadowAttachment);
            passes.BindAt(0, targets.GBuffer[1].Handle);
            passes.BindAt(1, targets.GBuffer[3].Handle);
            passes.BindAt(9, passes.Black);
            passes.BindAt(10, targets.ShadowCascades.Handle, TextureTarget.Texture2DArray);
            passes.BindAt(12, targets.LinearDepthHalf.Handle);
            passes.BindAt(14, passes.White);
            passes.BindAt(BotwPasses.IdUnit, targets.GBuffer[0].Handle);
            passes.BindAt(BotwPasses.LightAnalyzedUnit, passes.LightAnalyzed);
            passes.Draw(_shadowPass);
        }

        if (_lightPass is not null)
        {
            _targets.Bind(0, 1, 2, 3, 4);
            EnsureCube(frame.HemiSky, frame.HemiGround);
            passes.BindAt(0, targets.GBuffer[1].Handle);
            passes.BindAt(1, targets.GBuffer[3].Handle);
            passes.BindAt(2, targets.GBuffer[5].Handle);
            passes.BindAt(3, passes.White);
            passes.BindAt(5, targets.GBuffer[0].Handle);
            passes.BindAt(6, passes.White);
            passes.BindAt(8, passes.White);
            passes.BindAt(9, passes.Black);
            passes.BindAt(13, targets.LinearDepthHalf.Handle);
            passes.BindAt(14, passes.Black);
            passes.BindAt(16, _cube, TextureTarget.TextureCubeMap);
            passes.BindAt(BotwPasses.IdUnit, targets.GBuffer[0].Handle);
            passes.BindAt(BotwPasses.LightAnalyzedUnit, passes.LightAnalyzed);
            passes.Draw(_lightPass);
        }
        ClipOrigin.Game(gl, false);
        GLDiagnostics.CheckPass(gl, "BotW pre-shading");

        _targets.CopyToArray(DiffuseAttachment);
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Draws the one shadow cascade, and returns the matrix from view space to its texture space the shading passes read.</summary>
    Vector4[] DrawCascade(FrameContext frame)
    {
        var resources = services.Resources;
        var request = frame.Request;
        var (lo, hi) = ShadowSignatures.CombinedBounds(request.Actors, frame.Instances);
        var light = ShadowPass.BuildLightMatrices(lo, hi, frame.SunWorld);
        frame.LightMatrices = light;
        frame.ShadowBoundsLo = lo;
        frame.ShadowBoundsHi = hi;

        services.Profile.Camera(FrameUniformKeys.LightCamera, CameraData.ForLight(light, frame.Cam)).Bind(resources);
        _shadow.Run(resources, frame.Targets, frame.CastingGroups, services.Programs, cascade: 0);
        GLDiagnostics.CheckPass(services.Gl, "shadow cascade");
        resources.BindCamera(FrameUniformKeys.SceneCamera);

        // Clip space [-1, 1] to texture space [0, 1].
        var toTexture = Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f);
        return CameraData.Rows(frame.Cam.ViewInv * light.ViewProj * toTexture);
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
