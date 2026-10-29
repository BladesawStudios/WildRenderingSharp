using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The gloom's colours and patterns at a moment of its clock, as the game's renderer packs its parameters for the compose program (<c>cCompose</c>).</summary>
/// <remarks>
/// <see cref="DarkDump"/> is the fade start and end distance, the intensity and the least intensity; <see cref="BaseColor0"/> carries the black pattern's density
/// in W; <see cref="BaseColor1"/> is the swinging mix of the two mix colours; <see cref="Clock"/> is the three wrapped clocks the pattern, the colour swing and the
/// near wobble read.
/// </remarks>
public readonly record struct GloomLook(
    Vector4 DarkDump, Vector4 BaseColor0, Vector3 BaseColor1, Vector3 BlackColor, Vector2 Intensity,
    Vector2 RedPattern, Vector3 GrainColor, Vector2 GrainPattern, Vector3 Clock);
