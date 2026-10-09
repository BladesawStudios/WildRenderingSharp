using System.Numerics;
using WildRenderingSharp.Profiles.Botw.Ubos;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Tests;

public class PassUboSnapshotTests
{
    static readonly Vector4[] View =
        [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];

    static readonly Vector4[] Proj =
        [new(1.2f, 0, 0, 0), new(0, 2.1f, 0, 0), new(0, 0, -1.002f, -0.2002f), new(0, 0, -1, 0)];

    static readonly Vector4[] ViewProj =
        [new(0.96f, 0.12f, -0.24f, 3.6f), new(-0.21f, 1.89f, 0.63f, -3.15f), new(-0.2f, 0.3f, -0.85f, -12.2f), new(-0.2f, 0.3f, -0.85f, -12f)];

    static readonly Vector4[] ViewInv =
        [new(0.8f, -0.1f, 0.2f, 1f), new(0.1f, 0.9f, -0.3f, 2f), new(-0.2f, 0.3f, 0.85f, -9f)];

    static readonly Vector3 SunView = Vector3.Normalize(new(0.3f, 0.8f, 0.5f));
    static readonly Vector3 SunWorld = Vector3.Normalize(new(-0.4f, 0.2f, 0.9f));
    static readonly Vector2 Texel = new(1f / 1920, 1f / 1080);

    [Fact]
    public void Context_tileGrid() => Snapshot.Verify("context_tilegrid",
        ContextUbo.BuildForCamera(View, ViewProj, Proj, ViewInv, 16f / 9f, 0.55f, 0.1f, 5000f, Texel).WithTileGrid(3, 2).ToByteArray());

    [Fact]
    public void BotwContext_camera() => Snapshot.Verify("botw_context_camera",
        BotwContextUbo.ForCamera(View, ViewProj, Proj, ViewInv, 16f / 9f, 0.55f, 0.1f, 5000f, Texel).ToByteArray());

    [Fact]
    public void BotwContext_cascade() => Snapshot.Verify("botw_context_cascade",
        BotwContextUbo.ForCamera(View, ViewProj, Proj, ViewInv, 16f / 9f, 0.55f, 0.1f, 5000f, Texel).WithCascade(ViewProj, 1e9f, 2e9f).ToByteArray());

    [Fact]
    public void BotwEnv_lights() => Snapshot.Verify("botw_env_lights",
        BotwEnvUbo.From(SunView, new(2.1f, 1.9f, 1.6f), new(0.4f, 0.5f, 0.7f), new(0.2f, 0.18f, 0.15f), ViewInv, 1f / 2048).ToByteArray());

    [Fact]
    public void BotwSceneMat_lighting() => Snapshot.Verify("botw_scenemat", BotwSceneMatUbo.From(0.6f, 1.4f).ToByteArray());

    [Fact]
    public void CloudFade() => Snapshot.Verify("cloud_fade",
        CloudDistanceFade.BuildUbo(120f, 40f, true, 0.7f, new(1000f, 500f, 1000f), new(0.3f, 0.5f, 0.8f)));

    [Fact]
    public void CloudCommon()
    {
        var cloud = new EnvPalette.CloudLayer(true, 1.2f, new(0.1f, 0.2f, 0.3f), new(0.4f, 0.5f, 0.6f), new(0.7f, 0.8f, 0.9f), new(0.2f, 0.1f, 0.05f), 1.1f, 1.3f, 0.9f);
        var palette = EnvPaletteLibrary.Empty().Get(null);

        Snapshot.Verify("cloud_common", CloudDomePass.BuildCommonBlock(
            palette, cloud, CloudPostFxShared.Default, CloudPostFxLayer.Default, SunWorld, 12.5f, 900f, 1.4f));
    }

    [Fact]
    public void CloudView() => Snapshot.Verify("cloud_view", CloudDomePass.BuildViewBlock(
        Matrix4x4.CreateLookAt(new(1, 2, 3), new(4, 5, 6), Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 1.7f, 0.5f, 9000f),
        new(10f, 20f, 30f), CloudPostFxLayer.Default, 800f, 1f));

    [Fact]
    public void LensFlareRegister() => Snapshot.Verify("flare_register",
        LensFlarePass.BuildRegisterUbo(0.32f, new(0.2f, 0.3f, 0.4f), 0.28f, new(0.35f, 0.3f, 0.25f)));

    [Fact]
    public void SkyContext_plain() => Snapshot.Verify("sky_context",
        SkyPostFxPass.BuildContext(ViewInv, 0.6f, 0.34f, 0.2f));

    [Fact]
    public void SkyContext_fog() => Snapshot.Verify("sky_context_fog",
        SkyPostFxPass.BuildContext(ViewInv, 0.6f, 0.34f, 0.2f, new SkyPostFxPass.AdhocFog(0.5f, 1.1f, 0.02f, new(0.4f, 0.5f, 0.6f))));

    [Fact]
    public void SkyRenderInfo() => Snapshot.Verify("sky_renderinfo",
        SkyPostFxPass.BuildRenderInfo(SkyPostFx.Default, SunWorld, new Vector3(0.3f, 0.4f, 0.5f), 0.25f));

    [Fact]
    public void SkySizeInfo() => Snapshot.Verify("sky_sizeinfo", SkyPrecomputePass.BuildSizeInfo());

    [Fact]
    public void SkyConfig() => Snapshot.Verify("sky_config", SkyPrecomputePass.BuildConfig(SkyPostFx.Default, 3, 1.2f, 0.9f, 0.7f));

    [Fact]
    public void SkyLayerRenderInfo() => Snapshot.Verify("sky_layer_renderinfo", SkyPrecomputePass.BuildRenderInfo(5));

    [Fact]
    public void SkyBakeRenderInfo() => Snapshot.Verify("sky_bake_renderinfo", SkyPrecomputePass.BuildBakeRenderInfo(SkyPostFx.Default, SunWorld, SkyLook.Noon));
}
