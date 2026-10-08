using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>TotK <c>gsys_scene_material</c> ("SceneMat", decompiled as <c>fp_c13</c>), binding 10, 928 bytes.</summary>
public sealed class SceneMatUbo : IUboBlock
{
    public const int ByteSize = 928;

    /// <summary>
    /// Byte offsets for all 88 uniforms the archive's reflection declares, named after the BFSHA symbol (including its spelling,
    /// e.g. "Repeeat") so the table can be diffed against the source dump.
    /// </summary>
    public static class Fields
    {
        public const int DynamicToonLightAdjustForDemo = 0;      // vec4 - MIDS scale, see BuildFromLighting
        public const int DynamicCloudRatio = 16;
        public const int DynamicDepthShadowOff = 20;
        public const int DynamicProjShadowOff = 24;
        public const int DynamicRainRatio = 28;
        public const int DynamicRainfall = 32;
        public const int DynamicExposure = 36;
        public const int DynamicWorldShadowOff = 40;
        public const int DynamicBaseLightChangeRatio = 44;       // HIGHLIGHTS scale, see BuildFromLighting
        public const int DynamicMainRenderingLight = 48;
        public const int DynamicSkyOcclusionOff = 52;
        public const int DynamicDepthShadowScale = 56;
        public const int DynamicItemFilterAlpha = 60;
        public const int DynamicUiHighlight = 64;
        public const int ConstVanishingLocalPosScale = 68;
        public const int ConstVanishingPatternRepeeatRatio = 72;  // sic - matches the BFSHA symbol's own spelling
        public const int ConstVanishingPatternScrollSpeed = 76;
        public const int ConstVanishingColor0Start = 80;          // vec3
        public const int ConstVanishingWholeAlpha = 92;
        public const int ConstVanishingColor0End = 96;            // vec3
        public const int ConstVanishingShavingFetchScale = 108;
        public const int ConstVanishingColor1Start = 112;         // vec3
        public const int ConstVanishingShavingFetchOffset = 124;
        public const int ConstVanishingColor1End = 128;           // vec3
        public const int ConstVanishingBecomeMaxIntensityRatio = 140;
        public const int ConstVanishingPatternFetchScale = 144;   // vec3
        public const int DynamicWaterSpecularAoInCave = 156;
        public const int ConstReverseRecorderBaseColor = 160;     // vec4
        public const int ConstReverseRecorderEdgeColor = 176;     // vec4
        public const int ConstReverseRecorderStripeColor = 192;   // vec4
        public const int DynamicMonochromeSaturationForChara = 208;
        public const int DynamicMonochromeSaturationForNonChara = 212;
        public const int ConstWaterNormalOffsetScale = 216;
        public const int ConstXluShadowDiffuseAmbientScale = 220; // vec4, reflection-reported offset is not slot-aligned
        public const int DynamicDepthShadowSoftness = 236;
        public const int DynamicBehindScreenDistance = 240;
        public const int DynamicHorizonHeight = 244;
        public const int ConstSkyOcclusionRadius = 248;
        public const int ConstSkyOcclusionHeightDiff = 252;
        public const int DynamicObjectIdOutputCtrl = 256;         // vec4
        public const int DynamicDarkenvFieldColor = 272;          // vec4
        public const int DynamicDarkenvCharaColor = 288;          // vec4
        public const int DynamicDarkenvCharaSat = 304;
        public const int ConstAoMin = 308;
        public const int ConstEdgeAdjustQuatToHalf = 312;
        public const int ConstEdgeAdjustNormal = 316;
        public const int ConstBlueprintGhostShotageColor = 320;   // vec4
        public const int ConstBlueprintEdgeParam = 336;           // vec4
        public const int DynamicRoundDiscardParam = 352;          // vec4
        public const int ConstFigureParam = 368;                  // vec4
        public const int ConstBlueprintGhostAlternateColor = 384; // vec4
        public const int ConstBlueprintBaseAlbedo0 = 400;         // vec3
        public const int ConstBlueprintBaseAlbedoBlend = 412;
        public const int ConstBlueprintBaseAlbedo1 = 416;         // vec3
        public const int ConstEdgeClampQuatToHalf = 428;
        public const int ConstBlueprintEdgeColor = 432;           // vec3
        public const int ConstBlueprintSpecular = 444;
        public const int ConstBlueprintEdgeLightDarkRange = 448;
        public const int ConstBlueprintEdgeLightBrightRange = 452;
        public const int ConstSkyIslandShadowDensity = 456;
        public const int ConstSkyIslandShadowOffsetScale = 460;
        public const int ConstBlueprintEmissionColor = 464;       // vec3
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
        public const int SceneShadingInfoExposureBase = 576;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "SceneMat";
    public int BindingIndex => (int)TotkBindings.SceneMaterial;

    public static SceneMatUbo BuildFromLighting(
        Vector3 hemiSkyColor, Vector3 hemiGroundColor, float midScale, float highlightScale)
    {
        var scn = new SceneMatUbo();
        var b = scn._block;

        // Authored defaults from MasterMaterial (Model/SystemModel.SceneMaterial), verbatim.
        b.SetVectorAt(Fields.DynamicToonLightAdjustForDemo, 1f, 1f, 1f, 1f);
        b.SetFloatAt(Fields.DynamicCloudRatio, 1f);
        b.SetFloatAt(Fields.DynamicDepthShadowOff, 0f);
        b.SetFloatAt(Fields.DynamicProjShadowOff, 0f);
        b.SetFloatAt(Fields.DynamicRainRatio, 0f);
        b.SetFloatAt(Fields.DynamicRainfall, 0f);
        b.SetFloatAt(Fields.DynamicExposure, 0f);
        b.SetFloatAt(Fields.DynamicWorldShadowOff, 1f);
        b.SetFloatAt(Fields.DynamicBaseLightChangeRatio, 1f);
        b.SetFloatAt(Fields.DynamicMainRenderingLight, 1f);
        b.SetFloatAt(Fields.DynamicSkyOcclusionOff, 0f);
        b.SetFloatAt(Fields.DynamicDepthShadowScale, 1f);
        b.SetFloatAt(Fields.DynamicItemFilterAlpha, 0f);
        b.SetFloatAt(Fields.DynamicUiHighlight, 0f);
        b.SetFloatAt(Fields.ConstVanishingLocalPosScale, 0.1f);
        b.SetFloatAt(Fields.ConstVanishingPatternRepeeatRatio, 10f);
        b.SetFloatAt(Fields.ConstVanishingPatternScrollSpeed, 0.25f);
        b.SetVectorAt(Fields.ConstVanishingColor0Start, 1f, 1f, 5f);
        b.SetFloatAt(Fields.ConstVanishingWholeAlpha, 1f);
        b.SetVectorAt(Fields.ConstVanishingColor0End, 0.1f, 1f, 0.1f);
        b.SetFloatAt(Fields.ConstVanishingShavingFetchScale, 40f);
        b.SetVectorAt(Fields.ConstVanishingColor1Start, 1.5f, 6f, 1f);
        b.SetFloatAt(Fields.ConstVanishingShavingFetchOffset, 1f);
        b.SetVectorAt(Fields.ConstVanishingColor1End, 0.1f, 5f, 0.25f);
        b.SetFloatAt(Fields.ConstVanishingBecomeMaxIntensityRatio, 0.33f);
        b.SetVectorAt(Fields.ConstVanishingPatternFetchScale, 2f, 2f, 2f);
        b.SetFloatAt(Fields.DynamicWaterSpecularAoInCave, 1f);
        b.SetVectorAt(Fields.ConstReverseRecorderBaseColor, 1f, 0.9f, 0f, 0.3f);
        b.SetVectorAt(Fields.ConstReverseRecorderEdgeColor, 0f, 0f, 0f, 0f);
        b.SetVectorAt(Fields.ConstReverseRecorderStripeColor, 0f, 0f, 0f, 0f);
        b.SetFloatAt(Fields.DynamicMonochromeSaturationForChara, 1f);
        b.SetFloatAt(Fields.DynamicMonochromeSaturationForNonChara, 1f);
        b.SetFloatAt(Fields.ConstWaterNormalOffsetScale, 1f);
        b.SetVectorAt(Fields.ConstXluShadowDiffuseAmbientScale, 4f, 0f, 0f, 0f);
        b.SetFloatAt(Fields.DynamicDepthShadowSoftness, 0f);
        b.SetFloatAt(Fields.DynamicBehindScreenDistance, 24600f);
        b.SetFloatAt(Fields.DynamicHorizonHeight, 120f);
        b.SetFloatAt(Fields.ConstSkyOcclusionRadius, 2f);
        b.SetFloatAt(Fields.ConstSkyOcclusionHeightDiff, 64f);
        b.SetVectorAt(Fields.DynamicObjectIdOutputCtrl, 1f, 0f, 0f, 0f);
        b.SetVectorAt(Fields.DynamicDarkenvFieldColor, 0f, 0f, 0f, 0f);
        b.SetVectorAt(Fields.DynamicDarkenvCharaColor, 0f, 0f, 0f, 0.5f);
        b.SetFloatAt(Fields.DynamicDarkenvCharaSat, 0.2f);
        b.SetFloatAt(Fields.ConstAoMin, 0.32f);
        b.SetFloatAt(Fields.ConstEdgeAdjustQuatToHalf, 2f);
        b.SetFloatAt(Fields.ConstEdgeAdjustNormal, 5f);
        b.SetVectorAt(Fields.ConstBlueprintGhostShotageColor, 0.85f, 0.5f, 2f, 0.75f);
        b.SetVectorAt(Fields.ConstBlueprintEdgeParam, 0.5f, 0.5f, 0.75f, 3.5f);
        b.SetVectorAt(Fields.DynamicRoundDiscardParam, 0f, 0f, 0.1f, 0.1f);
        b.SetVectorAt(Fields.ConstFigureParam, 0.65f, 0.75f, 0.5f, 0.5f);
        b.SetVectorAt(Fields.ConstBlueprintGhostAlternateColor, 1.45f, 0.575f, 2f, 1f);
        b.SetVectorAt(Fields.ConstBlueprintBaseAlbedo0, 0.04f, 0.13f, 0.08f);
        b.SetFloatAt(Fields.ConstBlueprintBaseAlbedoBlend, 0.7f);
        b.SetVectorAt(Fields.ConstBlueprintBaseAlbedo1, 0.1f, 0.24f, 0.125f);
        b.SetFloatAt(Fields.ConstEdgeClampQuatToHalf, 1000f);
        b.SetVectorAt(Fields.ConstBlueprintEdgeColor, 0.117968f, 0.253972f, 0.134274f);
        b.SetFloatAt(Fields.ConstBlueprintSpecular, 1f);
        b.SetFloatAt(Fields.ConstBlueprintEdgeLightDarkRange, 0.7f);
        b.SetFloatAt(Fields.ConstBlueprintEdgeLightBrightRange, 0.6f);
        b.SetFloatAt(Fields.ConstSkyIslandShadowDensity, 0.925f);
        b.SetFloatAt(Fields.ConstSkyIslandShadowOffsetScale, 0.009f);
        b.SetVectorAt(Fields.ConstBlueprintEmissionColor, 0.01f, 1f, 0.2f);
        b.SetFloatAt(Fields.ConstMiasmaSpecularScale, 0.75f);
        b.SetFloatAt(Fields.ConstBlueprintAlbedoExposureCoef, 1f);
        b.SetFloatAt(Fields.ConstBlueprintAlbedoEmissionCoef, 0.5f);
        b.SetFloatAt(Fields.ConstBlueprintAlbedoEmissionExposureCoef, 0.1f);
        b.SetFloatAt(Fields.ConstAoSatSsao, 2f);
        b.SetFloatAt(Fields.DynamicResolutionRatio, 1f);
        b.SetFloatAt(Fields.ConstCubemapMainLightIntensity, 1f);
        b.SetFloatAt(Fields.ConstCubemapMainLightOcclusionIntensity, 0f);
        b.SetFloatAt(Fields.ConstAoSatLocalAo, 4f);
        b.SetFloatAt(Fields.ConstAoCaveDarkness, 1f);
        b.SetFloatAt(Fields.CubemapScatterDensity, 1f);
        b.SetFloatAt(Fields.ConstEdgeAdjustQuatNormal, 20f);
        b.SetFloatAt(Fields.ConstWaterPreventNldThreshold, 0.05f);
        b.SetFloatAt(Fields.ConstDepthShadowCharaPcf, 1f);
        b.SetFloatAt(Fields.ConstDepthShadowCharaBlurPcf, 2f);
        b.SetFloatAt(Fields.ConstDepthShadowCharaBlurScale, 1f);
        b.SetFloatAt(Fields.ConstDepthShadowReserved2, 0f);
        b.SetFloatAt(Fields.ConstDepthShadowReserved3, 0f);
        b.SetFloatAt(Fields.ConstDepthShadowReserved4, 0f);
        b.SetFloatAt(Fields.ConstDepthShadowReserved5, 0f);
        b.SetFloatAt(Fields.ConstDepthShadowReserved6, 0f);
        b.SetFloatAt(Fields.DynamicDebug0, 0f);
        b.SetFloatAt(Fields.DynamicDebug1, 0f);
        b.SetFloatAt(Fields.DynamicDebug2, 0f);
        b.SetFloatAt(Fields.DynamicDebug3, 0f);
        // The 88-float catch-all tail (576..927) is authored all-zero except its last component (.w = 1) in MasterMaterial. It stays zero: the slots with a confirmed reader (1, 16, 20, 54) are overridden right after, and the rest have no reader to justify a guess.

        // Overrides driven by live studio and lighting state, not authored constants.
        b.SetVectorAt(Fields.DynamicToonLightAdjustForDemo, midScale, midScale, midScale, 0f);
        b.SetFloatAt(Fields.DynamicBaseLightChangeRatio, highlightScale);

        // DynamicExposure's neutral is 1, not MasterMaterial's authored 0, and 0 is not a no-op: several G-buffer shaders fold it into a lerp. Enemy_MiasmaTentacle's skin computes
        // its emission scale as fma(fma(volumeMask.y, -DynamicExposure, DynamicExposure), 0.5, 0.5), a flat 0.5 at 0 instead of 1.0 for an unmasked pixel. Daylight palettes author
        // Exposure 1.0 and 1.0 is the identity for every use seen, so this is an engine-style per-frame override (compare game::gfx::ModelSceneExtension::setDynamicShadowParams).
        b.SetFloatAt(Fields.DynamicExposure, 1f);

        // SceneShadingInfoExposure[1] is added to the shadow term, clamp(shadow + this, 0, 1): 0 lets PreShadow act, 1 would unshadow the sun.
        b.SetFloatAt(Fields.SceneShadingInfoExposureBase + 4 * 1, 0f);
        // SceneShadingInfoExposure[16] and [20]: the deferred vertex shader lerps these by screen Y into an ambient term prog 6 multiplies albedo by, an ambient sky and ground pair hidden in the unnamed catch-all array.
        b.SetVectorAt(Fields.SceneShadingInfoExposureBase + 4 * 16, hemiSkyColor.X, hemiSkyColor.Y, hemiSkyColor.Z);
        b.SetVectorAt(Fields.SceneShadingInfoExposureBase + 4 * 20, hemiGroundColor.X, hemiGroundColor.Y, hemiGroundColor.Z);
        // SceneShadingInfoExposure[54] is another pow() exponent (see EnvUbo.PowExponentSlots); 1.0 for the same NaN-free reason.
        b.SetFloatAt(Fields.SceneShadingInfoExposureBase + 4 * 54, 1f);

        return scn;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
