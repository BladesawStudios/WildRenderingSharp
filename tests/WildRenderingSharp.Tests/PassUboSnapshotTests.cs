using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Profiles.Botw;
using WildRenderingSharp.Profiles.Botw.Ubos;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Profiles.Totk.Sky.Clouds;
using WildRenderingSharp.Profiles.Totk.Sky.LensFlare;
using WildRenderingSharp.Profiles.Totk.Sky.PostFx;
using WildRenderingSharp.Profiles.Totk.Sky.Precompute;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Tests;

public class PassUboSnapshotTests
{
    static readonly Vector3 SunView = Vector3.Normalize(new(0.3f, 0.8f, 0.5f));
    static readonly Vector3 SunWorld = Vector3.Normalize(new(-0.4f, 0.2f, 0.9f));

    static readonly SceneLightingData Lighting = new(
        SunView, SunWorld, new(2.1f, 1.9f, 1.6f), new(0.4f, 0.5f, 0.7f), new(0.2f, 0.18f, 0.15f), new(0.1f, 0.2f, 0.3f), 0.5f, 4096, 0.6f, 1.4f);

    static Vector4[] ViewInvRows => GpuMatrix.Rows(TestCamera.Default.ViewInv, 3);

    [Fact]
    public void Context_tileGrid() => Snapshot.Verify("context_tilegrid", TotkCameraUniforms.BuildTiled("camera", TestCamera.Default, 3, 2));

    [Fact]
    public void BotwContext_camera() => Snapshot.Verify("botw_context_camera", BotwUniforms.Camera("camera", TestCamera.Default));

    [Fact]
    public void BotwContext_cascade() => Snapshot.Verify("botw_context_cascade",
        BotwUniforms.CascadeCamera("camera", TestCamera.Default, TestCamera.ViewProjectionMatrix, 1e9f, 2e9f));

    [Fact]
    public void BotwEnv_lights() => Snapshot.Verify("botw_env_lights",
        BotwUniforms.Environment(SunView, Lighting.SunColor, Lighting.HemiSky, Lighting.HemiGround, TestCamera.InverseViewMatrix, 1f / 2048));

    [Fact]
    public void BotwSceneMat_lighting() => Snapshot.Verify("botw_scenemat", BotwUniforms.Lighting(Lighting)[0]);

    [Fact]
    public void CloudFade() => Snapshot.Verify("cloud_fade",
        CloudBlocks.BuildFade(120f, 40f, true, 0.7f, new(1000f, 500f, 1000f), new(0.3f, 0.5f, 0.8f)));

    [Fact]
    public void CloudCommon()
    {
        var cloud = new EnvPalette.CloudLayer(true, 1.2f, new(0.1f, 0.2f, 0.3f), new(0.4f, 0.5f, 0.6f), new(0.7f, 0.8f, 0.9f), new(0.2f, 0.1f, 0.05f), 1.1f, 1.3f, 0.9f);
        var palette = EnvPaletteLibrary.Empty().Get(null);

        Snapshot.Verify("cloud_common", CloudBlocks.BuildCommon(
            palette, cloud, CloudPostFxShared.Default, CloudPostFxLayer.Default, SunWorld, 12.5f, 900f, 1.4f));
    }

    [Fact]
    public void CloudView() => Snapshot.Verify("cloud_view", CloudBlocks.BuildView(
        Matrix4x4.CreateLookAt(new(1, 2, 3), new(4, 5, 6), Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 1.7f, 0.5f, 9000f),
        new(10f, 20f, 30f), CloudPostFxLayer.Default, 800f, 1f));

    [Fact]
    public void LensFlareRegister() => Snapshot.Verify("flare_register",
        LensFlareBlocks.BuildRegister(0.32f, new(0.2f, 0.3f, 0.4f), 0.28f, new(0.35f, 0.3f, 0.25f)));

    [Fact]
    public void SkyContext_plain() => Snapshot.Verify("sky_context", SkyPostFxBlocks.BuildContext(ViewInvRows, 0.6f, 0.34f, 0.2f));

    [Fact]
    public void SkyContext_fog() => Snapshot.Verify("sky_context_fog",
        SkyPostFxBlocks.BuildContext(ViewInvRows, 0.6f, 0.34f, 0.2f, new SkyPostFxPass.AdhocFog(0.5f, 1.1f, 0.02f, new(0.4f, 0.5f, 0.6f))));

    [Fact]
    public void SkyRenderInfo() => Snapshot.Verify("sky_renderinfo",
        SkyPostFxBlocks.BuildRenderInfo(SkyPostFx.Default, SunWorld, new Vector3(0.3f, 0.4f, 0.5f), 0.25f));

    [Fact]
    public void SkySizeInfo() => Snapshot.Verify("sky_sizeinfo", SkyPrecomputeBlocks.BuildSizeInfo());

    [Fact]
    public void SkyConfig() => Snapshot.Verify("sky_config", SkyPrecomputeBlocks.BuildConfig(SkyPostFx.Default, 3, 1.2f, 0.9f, 0.7f));

    [Fact]
    public void SkyLayerRenderInfo() => Snapshot.Verify("sky_layer_renderinfo", SkyPrecomputeBlocks.BuildLayerRenderInfo(5));

    [Fact]
    public void SkyBakeRenderInfo() => Snapshot.Verify("sky_bake_renderinfo", SkyPrecomputeBlocks.BuildBakeRenderInfo(SkyPostFx.Default, SunWorld, SkyLook.Noon));
}
