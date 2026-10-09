using WildRenderingSharp.Graphics;
using WildRenderingSharp.Assets;
namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Linear depth, screen-space shadow and ambient occlusion, and the light pre-pass.</summary>
public sealed class ScreenSpaceLightingStage(StageServices services, LinearDepthPass linearDepth) : IFrameStage, IDisposable
{
    readonly ScreenSpaceShadowAndAoPass _shadowAo = new(services.Gl);
    readonly LightPrePass _lightPrePass = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var resources = services.Resources;
        var targets = frame.Targets;
        var camera = frame.Camera;
        var cam = frame.Cam;

        linearDepth.Run(resources, targets, camera.NearPlane, camera.FarPlane);
        GLDiagnostics.CheckPass(services.Gl, "linear depth pass");

        float lightRadius = (frame.ShadowBoundsHi - frame.ShadowBoundsLo).Length() * 0.5f + 1e-4f;
        frame.ShadowAoParams = new ScreenSpaceShadowAndAoPass.Params(
            ViewInv: cam.ViewInv, LightViewProj: frame.LightMatrices.ViewProj,
            TanHalf: cam.TanHalf,
            SunWorld: frame.SunWorld, SunView: frame.SunView,
            Near: camera.NearPlane, Far: camera.FarPlane,
            ShadowBias: frame.Request.ShadowBias, ShadowTexel: 1f / RenderTargets.ShadowMapSize,
            ShadowTexelWorld: 2f * lightRadius / RenderTargets.ShadowMapSize, ShadowDepthRange: lightRadius * 5f - 0.01f,
            AoRadius: frame.Request.AoRadius, AoStrength: ScreenSpaceShadowAndAoPass.AoStrength,
            ShadowTexture: (frame.ShadowMapOverride ?? targets.ShadowMap).Handle,
            Cascades: frame.Cascades);
        _shadowAo.Run(resources, targets, frame.ShadowAoParams);
        GLDiagnostics.CheckPass(services.Gl, "screen-space shadow/AO pass");

        frame.LightPrePassParams = new LightPrePass.Params(
            ViewInv: cam.ViewInv, TanHalf: cam.TanHalf,
            Near: camera.NearPlane, Far: camera.FarPlane,
            SunWorld: frame.SunWorld, SunColor: frame.SunColor, HemiSky: frame.HemiSky, HemiGround: frame.HemiGround,
            Synthetic: frame.Lighting.SyntheticLightPrePass);
        _lightPrePass.Run(resources, targets, frame.LightPrePassParams);
        GLDiagnostics.CheckPass(services.Gl, "light pre-pass");
    }

    public void Repeat(FrameContext frame)
    {
        linearDepth.Run(services.Resources, frame.Targets, frame.Camera.NearPlane, frame.Camera.FarPlane);
        _shadowAo.Run(services.Resources, frame.Targets, frame.ShadowAoParams);
        _lightPrePass.Run(services.Resources, frame.Targets, frame.LightPrePassParams);
    }

    public void Dispose()
    {
        _shadowAo.Dispose();
        _lightPrePass.Dispose();
    }
}
