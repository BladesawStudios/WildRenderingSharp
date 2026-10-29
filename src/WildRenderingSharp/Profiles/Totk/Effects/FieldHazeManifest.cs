using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>One of the field's haze emitters: a sphere of sprites that follows the camera, as preparation recorded it.</summary>
/// <param name="Offset">How far ahead of the camera the sphere's centre is, in metres: the radius for a sphere that touches the camera, zero for one around it.</param>
/// <param name="Radius">Radius of the sphere the sprites are scattered in, in metres.</param>
/// <param name="Particles">How many sprites are alive at once.</param>
/// <param name="Area">Cross-section of one sprite facing the camera, in square metres.</param>
/// <param name="Alpha">The sprite's opacity averaged over its life and its texels.</param>
/// <param name="NearStart">The distance ahead of the camera, in metres of view depth, at which a sprite begins to fade in.</param>
/// <param name="NearEnd">Where it is wholly faded in.</param>
/// <param name="FarStart">Where it begins to fade out, or zero when it does not.</param>
/// <param name="FarEnd">Where it is wholly faded out.</param>
/// <param name="SoftDistance">How far in front of the scene's depth, in metres, a sprite's opacity is reduced to nothing at the surface.</param>
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
