using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Linear depth, screen-space shadow and ambient occlusion, and the light pre-pass.</summary>
internal sealed class ScreenSpaceLightingStage(StageServices services, LinearDepthPass linearDepth) : IFrameStage, IDisposable
{
    readonly ScreenSpaceShadowAndAoPass _shadowAo = new(services.Gl);
    readonly LightPrePass _lightPrePass = new(services.Gl);
    ScreenSpaceShadowAndAoPass.Params _shadowAoParams;
    LightPrePass.Params _lightPrePassParams;

    public void Run(FrameContext frame)
    {
        var resources = services.Resources;
        var targets = frame.Targets;
        var camera = frame.Camera;
        var cam = frame.Setup.Cam;

        linearDepth.Run(resources, targets, camera.NearPlane, camera.FarPlane);
        GLDiagnostics.CheckPass(services.Gl, "linear depth pass");

        float lightRadius = (frame.Shadow.BoundsHi - frame.Shadow.BoundsLo).Length() * 0.5f + 1e-4f;
        _shadowAoParams = new ScreenSpaceShadowAndAoPass.Params(
            ViewInv: cam.ViewInv, LightViewProj: frame.Shadow.Light.ViewProj,
            TanHalf: cam.TanHalf,
            SunWorld: frame.Setup.SunWorld, SunView: frame.Setup.SunView,
            Near: camera.NearPlane, Far: camera.FarPlane,
            ShadowBias: frame.Request.ShadowBias, ShadowTexel: 1f / RenderTargets.ShadowMapSize,
            ShadowTexelWorld: 2f * lightRadius / RenderTargets.ShadowMapSize, ShadowDepthRange: lightRadius * 5f - 0.01f,
            AoRadius: frame.Request.AoRadius, AoStrength: ScreenSpaceShadowAndAoPass.AoStrength,
            ShadowTexture: (frame.ShadowMapOverride ?? targets.ShadowMap).Handle,
            Cascades: frame.Shadow.Cascades);
        _shadowAo.Run(resources, targets, _shadowAoParams);
        GLDiagnostics.CheckPass(services.Gl, "screen-space shadow/AO pass");

        _lightPrePassParams = new LightPrePass.Params(
            ViewInv: cam.ViewInv, TanHalf: cam.TanHalf,
            Near: camera.NearPlane, Far: camera.FarPlane,
            SunWorld: frame.Setup.SunWorld, SunColor: frame.Setup.SunColor, HemiSky: frame.Setup.HemiSky, HemiGround: frame.Setup.HemiGround,
            Synthetic: frame.Lighting.SyntheticLightPrePass);
        _lightPrePass.Run(resources, targets, _lightPrePassParams);
        GLDiagnostics.CheckPass(services.Gl, "light pre-pass");
    }

    public void Repeat(FrameContext frame)
    {
        linearDepth.Run(services.Resources, frame.Targets, frame.Camera.NearPlane, frame.Camera.FarPlane);
        _shadowAo.Run(services.Resources, frame.Targets, _shadowAoParams);
        _lightPrePass.Run(services.Resources, frame.Targets, _lightPrePassParams);
    }

    public void Dispose()
    {
        _shadowAo.Dispose();
        _lightPrePass.Dispose();
    }
}
