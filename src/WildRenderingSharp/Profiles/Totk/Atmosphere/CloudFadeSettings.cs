namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// WildRenderingSharp's own distance fade for the cloud dome - see <c>CloudDistanceFade</c> for why it exists alongside the game's
/// own (which has never produced a visible falloff here).
/// </summary>
public readonly record struct CloudFadeSettings(
    float StartDistance = 12000f,
    float Ramp = 1.5f,
    bool Exponential = true,
    float Strength = 1f);
