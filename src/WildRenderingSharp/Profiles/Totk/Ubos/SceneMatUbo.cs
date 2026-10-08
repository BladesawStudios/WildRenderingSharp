using System.Numerics;
using WildRenderingSharp.Shaders.Common;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK <c>gsys_scene_material</c> ("SceneMat", decompiled as <c>fp_c13</c>), binding 10, 928
/// bytes. Unlike <see cref="ContextUbo"/>/<see cref="EnvUbo"/>, every field's name and byte
/// offset here comes straight from the "material" shading model's own BFSHA reflection
/// (regenerate with <c>ShaderLibrary.CompileTool --dump-uniform-blocks</c>) - not
/// reverse-engineered guesswork, so <see cref="Fields"/> is a faithful, complete transcription of
/// that reflection rather than a "what we've confirmed so far" subset. As of this writing, every
/// field's real DEFAULT VALUE is known too (see <see cref="BuildFromLighting"/>'s own remarks) -
/// unlike Context/Env, this isn't scene state the engine writes every frame by name; it comes
/// from a real, separate romfs model asset's authored material parameters.
///
/// Fields are addressed by byte offset (<see cref="Std140Block.GetFloatAt"/>/
/// <see cref="Std140Block.SetVectorAt"/>) rather than 16-byte slot, because several of them
/// (e.g. <see cref="Fields.ConstXluShadowDiffuseAmbientScale"/>) sit at a reflection-reported
/// offset that isn't itself slot-aligned.
/// </summary>
public sealed class SceneMatUbo : IUboBlock
{
    public const int ByteSize = 928;

    /// <summary>
    /// Byte offsets for all 88 uniforms the archive's reflection declares, named exactly after
    /// the BFSHA symbol (including its literal spelling, e.g. "Repeeat") so this table can be
    /// diffed against the source dump directly.
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
        /// <summary>float[88], 352 bytes (576..927) - the reflection's own catch-all tail. Index with <c>SceneShadingInfoExposureBase + 4*i</c>.</summary>
        public const int SceneShadingInfoExposureBase = 576;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "SceneMat";
    public int BindingIndex => 10;
    public int SizeBytes => ByteSize;

    /// <summary>
    /// Starts from the REAL authored defaults for all 88 fields, then overlays the handful WildRenderingSharp
    /// drives from live lighting/studio state - the exact same "defaults, then overlay" shape as
    /// every other UBO in this codebase (<c>BuildMaterialUbo.BuildBlock</c> for a material's own
    /// <c>gsys_material</c>, most directly).
    ///
    /// Those defaults are NOT invented: traced via Ghidra to <c>gsys::ModelScene::initialize_</c>,
    /// which loads a real, separate model - <c>Model/SystemModel.SceneMaterial.bfres.mc</c>,
    /// found by searching romfs for the resource name "SceneMaterial" the scene looks up - through
    /// <c>gsys::ModelNW::initialize</c>, then passes that loaded model into every render context's
    /// <c>gsys::ModelRenderContext::setSceneMaterial</c>. So unlike Context/Env ("Dynamic" scene
    /// state written by engine code every frame, by field name - see the "Dynamic" fields' own
    /// remarks below for the one case that IS confirmed this way), most of SceneMat's fields -
    /// especially every "Const"-prefixed one - are just an ordinary material's authored
    /// <c>ShaderParams</c> on this one dedicated model ("MasterMaterial"), read with
    /// <c>ShaderLibrary.CompileTool --dump-scene-material</c> (new flag, see
    /// <c>BuildMaterialUbo.DumpSceneMaterial</c>) - the exact same name-join mechanism
    /// <c>BuildMaterialUbo.BuildBlock</c> already uses for <c>gsys_material</c>, just pointed at
    /// this model's own block instead. All 88 fields matched by name; nothing here is guessed.
    ///
    /// This directly explains the TotK "Blueprint" (Zonai schematic/ghost-construct) rendering
    /// bug this session chased at length: every <c>ConstBlueprint*</c> colour authored here is a
    /// shade of GREEN (e.g. <see cref="Fields.ConstBlueprintEmissionColor"/> = (0.01, 1, 0.2)) -
    /// the real, recognisable Zonai-schematic green tint - and WildRenderingSharp leaving them all at zero
    /// made that formula collapse into a nonsensical NEGATIVE result instead, which is what
    /// actually produced the reported "pure cyan" look on Enemy_MiasmaTentacle's Mt_Skin once its
    /// forward (<c>gsys_assign_material</c>) program was enabled (that material authors
    /// <c>p_blue_print_alpha = 1.0</c>, genuinely turning this whole code path on).
    /// </summary>
    public static SceneMatUbo BuildFromLighting(
        Vector3 hemiSkyColor, Vector3 hemiGroundColor, float midScale, float highlightScale)
    {
        var scn = new SceneMatUbo();
        var b = scn._block;

        // ---- Real authored defaults from MasterMaterial (Model/SystemModel.SceneMaterial), verbatim ----
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
        // The 88-float catch-all tail (576..927) is authored all-zero except its very last
        // component (index 3, .w = 1) in MasterMaterial - kept zero here too, since the specific
        // slots this codebase has confirmed a real reader for (1/16/20/54, immediately below) are
        // overridden right after this anyway, and the rest have no confirmed reader to justify a
        // non-zero guess.

        // ---- WildRenderingSharp-driven overrides: live studio/lighting state, not authored constants ----
        b.SetVectorAt(Fields.DynamicToonLightAdjustForDemo, midScale, midScale, midScale, 0f);
        b.SetFloatAt(Fields.DynamicBaseLightChangeRatio, highlightScale);

        // DynamicExposure's neutral is 1, not 0 (MasterMaterial's own authored default above IS
        // 0), and leaving it at 0 is not a no-op - several G-buffer shaders fold it into a lerp
        // rather than a multiply. Enemy_MiasmaTentacle's skin computes its emission scale as
        //     fma(fma(volumeMask.y, -DynamicExposure, DynamicExposure), 0.5, 0.5)
        // which collapses to a flat 0.5 at DynamicExposure = 0 instead of resolving to 1.0 for an
        // un-masked pixel. Every real palette authors Exposure 1.0 for daylight, and 1.0 is the
        // identity for every use of it seen in the decompiled shaders - this is a genuine engine
        // per-frame "Dynamic" override on top of MasterMaterial's own bootstrap default, mirroring
        // how the real engine's Dynamic fields work in general (see DynamicDepthShadowScale/Off's
        // own confirmed runtime writer, game::gfx::ModelSceneExtension::setDynamicShadowParams).
        b.SetFloatAt(Fields.DynamicExposure, 1f);

        // SceneShadingInfoExposure[1]: ADDED to the shadow term (temp = clamp(shadow + this, 0, 1)).
        // 0 lets the real PreShadow buffer do something; 1 would unconditionally unshadow the sun.
        b.SetFloatAt(Fields.SceneShadingInfoExposureBase + 4 * 1, 0f);
        // SceneShadingInfoExposure[16]/[20]: the deferred VERTEX shader lerps these by screen Y
        // into an ambient term prog 6 multiplies albedo by - a genuine ambient sky/ground colour
        // pair hiding in the reflection's unnamed catch-all array, not a scalar.
        b.SetVectorAt(Fields.SceneShadingInfoExposureBase + 4 * 16, hemiSkyColor.X, hemiSkyColor.Y, hemiSkyColor.Z);
        b.SetVectorAt(Fields.SceneShadingInfoExposureBase + 4 * 20, hemiGroundColor.X, hemiGroundColor.Y, hemiGroundColor.Z);
        // SceneShadingInfoExposure[54]: another pow() exponent (see EnvUbo.PowExponentSlots for why
        // zero is dangerous) landing in the same unnamed tail; 1.0 for the same NaN-free reason.
        b.SetFloatAt(Fields.SceneShadingInfoExposureBase + 4 * 54, 1f);

        return scn;
    }

    public void WriteTo(Span<byte> destination) => _block.WriteTo(destination);
    public byte[] ToByteArray() => _block.ToByteArray();
}
