using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>One of the field's haze emitters: a shell of sprites that follows the camera, as preparation recorded it.</summary>
/// <remarks>
/// Offset and Radius place the sphere the sprites fill, ahead of the camera, in metres; Particles, Area and Alpha say how many are alive, how much of the view one covers and
/// how opaque one is averaged over its life and its texels. A sprite fades in over NearStart to NearEnd metres of view depth and out over FarStart to FarEnd (zero for no fade
/// out), and fades out against the scene's depth over its last SoftDistance metres.
/// </remarks>
public sealed record FieldHazeLevel(float Offset, float Radius, float Particles, float Area, float Alpha,
    float NearStart, float NearEnd, float FarStart, float FarEnd, float SoftDistance);

/// <summary>The game's field haze, the sprites that veil distant terrain in a pale warm colour, as preparation recorded it from the effect file.</summary>
/// <remarks>The levels are spheres of growing radius, the nearest around the player; their colour is shared.</remarks>
public sealed record FieldHazeManifest(float[] Color, FieldHazeLevel[] Levels)
{
    const string FileName = "field_haze_v2.json";

    public static string Path(string effectsDirectory) => System.IO.Path.Combine(effectsDirectory, FileName);

    public static bool IsPrepared(string effectsDirectory) => File.Exists(Path(effectsDirectory));

    // Null when the haze was not prepared.
    public static FieldHazeManifest? Load(string effectsDirectory) =>
        IsPrepared(effectsDirectory) ? JsonSerializer.Deserialize<FieldHazeManifest>(File.ReadAllText(Path(effectsDirectory))) : null;
}
