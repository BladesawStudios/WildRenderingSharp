using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Games.Totk.Effects;

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
    public const string FileName = "field_haze.bin";

    public static bool IsPrepared(IContentFiles effects) => effects.Exists(FileName);

    // Null when the haze was not prepared.
    public static FieldHazeManifest? Load(IContentFiles effects) => IsPrepared(effects) ? FromBytes(effects.ReadBytes(FileName)) : null;

    // The haze as bytes: the colour as an array, then the number of levels and the ten numbers of each.
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            ParticleEmitterManifest.WriteFloats(w, Color);
            w.Write(Levels.Length);
            foreach (FieldHazeLevel l in Levels)
                foreach (float v in new[] { l.Offset, l.Radius, l.Particles, l.Area, l.Alpha, l.NearStart, l.NearEnd, l.FarStart, l.FarEnd, l.SoftDistance })
                    w.Write(v);
        }

        return stream.ToArray();
    }

    public static FieldHazeManifest FromBytes(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        float[] color = ParticleEmitterManifest.ReadFloats(r);
        var levels = new FieldHazeLevel[r.ReadInt32()];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = new FieldHazeLevel(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        return new FieldHazeManifest(color, levels);
    }
}
