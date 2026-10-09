using WildRenderingSharp.Graphics.Ubos;
using static WildRenderingSharp.Profiles.Totk.Ubos.TotkSceneMaterialLayout;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>The authored values of <c>Model/SystemModel.SceneMaterial</c>'s material for every field the lighting does not drive; fields not listed are authored zero.</summary>
static class TotkSceneMaterialDefaults
{
    static readonly (int Offset, float[] Values)[] Authored =
    [
        (ConstVanishingLocalPosScale, [0.1f]),
        (ConstVanishingPatternRepeeatRatio, [10f]),
        (ConstVanishingPatternScrollSpeed, [0.25f]),
        (ConstVanishingColor0Start, [1f, 1f, 5f]),
        (ConstVanishingWholeAlpha, [1f]),
        (ConstVanishingColor0End, [0.1f, 1f, 0.1f]),
        (ConstVanishingShavingFetchScale, [40f]),
        (ConstVanishingColor1Start, [1.5f, 6f, 1f]),
        (ConstVanishingShavingFetchOffset, [1f]),
        (ConstVanishingColor1End, [0.1f, 5f, 0.25f]),
        (ConstVanishingBecomeMaxIntensityRatio, [0.33f]),
        (ConstVanishingPatternFetchScale, [2f, 2f, 2f]),
        (DynamicWaterSpecularAoInCave, [1f]),
        (ConstReverseRecorderBaseColor, [1f, 0.9f, 0f, 0.3f]),
        (DynamicMonochromeSaturationForChara, [1f]),
        (DynamicMonochromeSaturationForNonChara, [1f]),
        (ConstWaterNormalOffsetScale, [1f]),
        (ConstXluShadowDiffuseAmbientScale, [4f, 0f, 0f, 0f]),
        (DynamicBehindScreenDistance, [24600f]),
        (DynamicHorizonHeight, [120f]),
        (ConstSkyOcclusionRadius, [2f]),
        (ConstSkyOcclusionHeightDiff, [64f]),
        (DynamicObjectIdOutputCtrl, [1f, 0f, 0f, 0f]),
        (DynamicDarkenvCharaColor, [0f, 0f, 0f, 0.5f]),
        (DynamicDarkenvCharaSat, [0.2f]),
        (ConstAoMin, [0.32f]),
        (ConstEdgeAdjustQuatToHalf, [2f]),
        (ConstEdgeAdjustNormal, [5f]),
        (ConstBlueprintGhostShotageColor, [0.85f, 0.5f, 2f, 0.75f]),
        (ConstBlueprintEdgeParam, [0.5f, 0.5f, 0.75f, 3.5f]),
        (DynamicRoundDiscardParam, [0f, 0f, 0.1f, 0.1f]),
        (ConstFigureParam, [0.65f, 0.75f, 0.5f, 0.5f]),
        (ConstBlueprintGhostAlternateColor, [1.45f, 0.575f, 2f, 1f]),
        (ConstBlueprintBaseAlbedo0, [0.04f, 0.13f, 0.08f]),
        (ConstBlueprintBaseAlbedoBlend, [0.7f]),
        (ConstBlueprintBaseAlbedo1, [0.1f, 0.24f, 0.125f]),
        (ConstEdgeClampQuatToHalf, [1000f]),
        (ConstBlueprintEdgeColor, [0.117968f, 0.253972f, 0.134274f]),
        (ConstBlueprintSpecular, [1f]),
        (ConstBlueprintEdgeLightDarkRange, [0.7f]),
        (ConstBlueprintEdgeLightBrightRange, [0.6f]),
        (ConstSkyIslandShadowDensity, [0.925f]),
        (ConstSkyIslandShadowOffsetScale, [0.009f]),
        (ConstBlueprintEmissionColor, [0.01f, 1f, 0.2f]),
        (ConstMiasmaSpecularScale, [0.75f]),
        (ConstBlueprintAlbedoExposureCoef, [1f]),
        (ConstBlueprintAlbedoEmissionCoef, [0.5f]),
        (ConstBlueprintAlbedoEmissionExposureCoef, [0.1f]),
        (ConstAoSatSsao, [2f]),
        (DynamicResolutionRatio, [1f]),
        (ConstCubemapMainLightIntensity, [1f]),
        (ConstAoSatLocalAo, [4f]),
        (ConstAoCaveDarkness, [1f]),
        (CubemapScatterDensity, [1f]),
        (ConstEdgeAdjustQuatNormal, [20f]),
        (ConstWaterPreventNldThreshold, [0.05f]),
        (ConstDepthShadowCharaPcf, [1f]),
        (ConstDepthShadowCharaBlurPcf, [2f]),
        (ConstDepthShadowCharaBlurScale, [1f]),
    ];

    public static void Write(UboWriter block)
    {
        foreach (var (offset, values) in Authored)
            block.SetAt(offset, values);
    }
}
