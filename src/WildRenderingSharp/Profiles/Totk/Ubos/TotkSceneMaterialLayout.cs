using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK's <c>gsys_scene_material</c>, decompiled as <c>fp_c13</c>: byte offsets of the uniforms the shader archive's reflection declares,
/// named after its symbols (spelling included) so the table can be diffed against the dump. The first ones are in <see cref="GsysSceneMaterial"/>.
/// </summary>
static class TotkSceneMaterialLayout
{
    public static readonly UboSpec Spec = new("gsys_scene_material", TotkBindings.SceneMaterial, 928);

    public const int ConstVanishingLocalPosScale = 68;
    public const int ConstVanishingPatternRepeeatRatio = 72;
    public const int ConstVanishingPatternScrollSpeed = 76;
    public const int ConstVanishingColor0Start = 80;
    public const int ConstVanishingWholeAlpha = 92;
    public const int ConstVanishingColor0End = 96;
    public const int ConstVanishingShavingFetchScale = 108;
    public const int ConstVanishingColor1Start = 112;
    public const int ConstVanishingShavingFetchOffset = 124;
    public const int ConstVanishingColor1End = 128;
    public const int ConstVanishingBecomeMaxIntensityRatio = 140;
    public const int ConstVanishingPatternFetchScale = 144;
    public const int DynamicWaterSpecularAoInCave = 156;
    public const int ConstReverseRecorderBaseColor = 160;
    public const int ConstReverseRecorderEdgeColor = 176;
    public const int ConstReverseRecorderStripeColor = 192;
    public const int DynamicMonochromeSaturationForChara = 208;
    public const int DynamicMonochromeSaturationForNonChara = 212;
    public const int ConstWaterNormalOffsetScale = 216;
    public const int ConstXluShadowDiffuseAmbientScale = 220;
    public const int DynamicDepthShadowSoftness = 236;
    public const int DynamicBehindScreenDistance = 240;
    public const int DynamicHorizonHeight = 244;
    public const int ConstSkyOcclusionRadius = 248;
    public const int ConstSkyOcclusionHeightDiff = 252;
    public const int DynamicObjectIdOutputCtrl = 256;
    public const int DynamicDarkenvFieldColor = 272;
    public const int DynamicDarkenvCharaColor = 288;
    public const int DynamicDarkenvCharaSat = 304;
    public const int ConstAoMin = 308;
    public const int ConstEdgeAdjustQuatToHalf = 312;
    public const int ConstEdgeAdjustNormal = 316;
    public const int ConstBlueprintGhostShotageColor = 320;
    public const int ConstBlueprintEdgeParam = 336;
    public const int DynamicRoundDiscardParam = 352;
    public const int ConstFigureParam = 368;
    public const int ConstBlueprintGhostAlternateColor = 384;
    public const int ConstBlueprintBaseAlbedo0 = 400;
    public const int ConstBlueprintBaseAlbedoBlend = 412;
    public const int ConstBlueprintBaseAlbedo1 = 416;
    public const int ConstEdgeClampQuatToHalf = 428;
    public const int ConstBlueprintEdgeColor = 432;
    public const int ConstBlueprintSpecular = 444;
    public const int ConstBlueprintEdgeLightDarkRange = 448;
    public const int ConstBlueprintEdgeLightBrightRange = 452;
    public const int ConstSkyIslandShadowDensity = 456;
    public const int ConstSkyIslandShadowOffsetScale = 460;
    public const int ConstBlueprintEmissionColor = 464;
    public const int ConstMiasmaSpecularScale = 476;
    public const int ConstBlueprintAlbedoExposureCoef = 480;
    public const int ConstBlueprintAlbedoEmissionCoef = 484;
    public const int ConstBlueprintAlbedoEmissionExposureCoef = 488;
    public const int ConstAoSatSsao = 492;
    public const int DynamicResolutionRatio = 496;
    public const int ConstCubemapMainLightIntensity = 500;
    public const int ConstCubemapMainLightOcclusionIntensity = 504;
    public const int ConstAoSatLocalAo = 508;
    public const int ConstAoCaveDarkness = 512;
    public const int CubemapScatterDensity = 516;
    public const int ConstEdgeAdjustQuatNormal = 520;
    public const int ConstWaterPreventNldThreshold = 524;
    public const int ConstDepthShadowCharaPcf = 528;
    public const int ConstDepthShadowCharaBlurPcf = 532;
    public const int ConstDepthShadowCharaBlurScale = 536;
    public const int ConstDepthShadowReserved2 = 540;
    public const int ConstDepthShadowReserved3 = 544;
    public const int ConstDepthShadowReserved4 = 548;
    public const int ConstDepthShadowReserved5 = 552;
    public const int ConstDepthShadowReserved6 = 556;
    public const int DynamicDebug0 = 560;
    public const int DynamicDebug1 = 564;
    public const int DynamicDebug2 = 568;
    public const int DynamicDebug3 = 572;

    // An 88-float array the archive leaves unnamed; its entries 16, 20 and 54 have confirmed readers.
    public const int SceneShadingInfoExposureBase = 576;
    public const int SceneShadingSkyAmbient = SceneShadingInfoExposureBase + 4 * 16;
    public const int SceneShadingGroundAmbient = SceneShadingInfoExposureBase + 4 * 20;
    public const int SceneShadingPowExponent = SceneShadingInfoExposureBase + 4 * 54;
}
