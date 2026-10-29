namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The textures the gloom programs sample, as handles in the renderer's GL context: five 3D noise volumes and the Depths' two altitude maps.</summary>
/// <remarks>The altitude maps are null where the host has none, and the Depths' altitude gloom is not drawn without them.</remarks>
public sealed record GloomTextures(
    uint GradientSimple, uint GradientFbm, uint GradientRidge, uint WorleySimple, uint VoronoiseCloud, uint? Altitude, uint? Altitude2);
