namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// WildRenderingSharp's own distance fade for the cloud dome - see <c>CloudDistanceFade</c> for
/// why it exists alongside the game's own (which has never produced a visible falloff here).
/// </summary>
/// <param name="StartDistance">World units at which the fade begins. The dome is ~26500 across, so
/// the interesting range is thousands, not units.</param>
/// <param name="Ramp">How quickly it falls off past that. Higher fades harder.</param>
/// <param name="Exponential">Exponential falloff (never fully reaches zero) rather than a linear ramp.</param>
/// <param name="Strength">0 disables the fade entirely, leaving the shader's own behaviour untouched.</param>
public readonly record struct CloudFadeSettings(
    float StartDistance = 12000f,
    float Ramp = 1.5f,
    bool Exponential = true,
    float Strength = 1f);
