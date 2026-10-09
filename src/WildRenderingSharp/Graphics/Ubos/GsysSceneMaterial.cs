namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>
/// The byte offsets at the start of <c>gsys_scene_material</c> that both games' blocks share, named after the shader archive's symbols,
/// and the write that gives them the values the lighting drives.
/// </summary>
public static class GsysSceneMaterial
{
    public const int DynamicToonLightAdjustForDemo = 0;
    public const int DynamicCloudRatio = 16;
    public const int DynamicDepthShadowOff = 20;
    public const int DynamicProjShadowOff = 24;
    public const int DynamicRainRatio = 28;
    public const int DynamicRainfall = 32;
    public const int DynamicExposure = 36;
    public const int DynamicWorldShadowOff = 40;
    public const int DynamicBaseLightChangeRatio = 44;
    public const int DynamicMainRenderingLight = 48;
    public const int DynamicSkyOcclusionOff = 52;
    public const int DynamicDepthShadowScale = 56;
    public const int DynamicItemFilterAlpha = 60;
    public const int DynamicUiHighlight = 64;

    // The two scales are the toon lighting's mid-tone and highlight scales.
    public static void WriteLighting(UboWriter block, float midScale, float highlightScale)
    {
        block.SetAt(DynamicToonLightAdjustForDemo, midScale, midScale, midScale, 0f);
        block.SetAt(DynamicCloudRatio, 1f);
        block.SetAt(DynamicExposure, 1f);
        block.SetAt(DynamicWorldShadowOff, 1f);
        block.SetAt(DynamicBaseLightChangeRatio, highlightScale);
        block.SetAt(DynamicMainRenderingLight, 1f);
        block.SetAt(DynamicDepthShadowScale, 1f);
    }
}
