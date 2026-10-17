using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>One of the field's haze emitters: a sphere of sprites that follows the camera, as preparation recorded it.</summary>
/// <param name="Offset">How far ahead of the camera the sphere's centre is, in metres: the radius for a sphere that touches the camera, zero for one around it.</param>
/// <param name="Radius">Radius of the sphere the sprites are scattered in, in metres.</param>
/// <param name="Particles">How many sprites are alive at once.</param>
/// <param name="Area">Cross-section of one sprite facing the camera, in square metres.</param>
/// <param name="Alpha">The sprite's opacity averaged over its life.</param>
public sealed record FieldHazeLevel(float Offset, float Radius, float Particles, float Area, float Alpha);

/// <summary>The game's field haze, the sprites that veil distant terrain in a pale warm colour, as preparation recorded it from the effect file.</summary>
/// <remarks>The levels are spheres of growing radius, the nearest around the player; their colour is shared.</remarks>
public sealed record FieldHazeManifest(float[] Color, FieldHazeLevel[] Levels)
{
    const string FileName = "field_haze.json";

    public static string Path(string effectsDirectory) => System.IO.Path.Combine(effectsDirectory, FileName);

    public static bool IsPrepared(string effectsDirectory) => File.Exists(Path(effectsDirectory));

    // Null when the haze was not prepared.
    public static FieldHazeManifest? Load(string effectsDirectory) =>
        IsPrepared(effectsDirectory) ? JsonSerializer.Deserialize<FieldHazeManifest>(File.ReadAllText(Path(effectsDirectory))) : null;
}
