using System.Text.Json;
using PtclSharp;
using PtclSharp.Layout;
using WildRenderingSharp.Profiles.Totk.Effects;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>
/// The haze the game veils the distance with: sprites in nested spheres around the camera, whose sizes, counts and opacities
/// the live half turns into how much of the view each level covers.
/// </summary>
public static class TotkFieldHazeEffect
{
    // The emitter sets of the field haze, nearest sphere first; each holds one emitter, the one the main field uses.
    static readonly string[] Sets = ["Env_FieldHaze_Interaction_Lv01", "FieldHaze_Lv02", "FieldHaze_Lv03", "FieldHaze_Lv04", "FieldHaze_Lv05", "FieldHaze_Lv06", "FieldHaze_Lv07"];

    // The mode of a track that is a table of keys, in which the engine interpolates between them.
    const int KeyedMode = 2;

    // How finely a keyed opacity is averaged over the sprite's life.
    const int LifeSamples = 64;

    // How many pairs of texels the sprite's coverage is averaged over, and the seed that picks them.
    const int TexelPairs = 1 << 15;
    const int TexelSeed = 0x48A2E;

    public static bool IsExported(CacheLayout cache) => FieldHazeManifest.IsPrepared(cache.Effects);

    public static void Export(IRomAccess rom, CacheLayout cache)
    {
        var file = TotkEffectFile.Open(rom, "static");
        var (resource, modelIds) = file.Models();
        var levels = new List<FieldHazeLevel>();
        float[] color = [];
        foreach (string set in Sets)
        {
            var fields = Emitter(file, set);
            // The engine copies a constant track's value into its first key when the emitter is made, and its programs read the keys.
            TotkEmitterConstants.Apply(fields);
            levels.Add(Level(rom, file, fields, TotkEffectMesh.Read(resource, modelIds.ToList().IndexOf((uint)fields.GetUInt64("g3d_primitive_idx")))));
            color = [fields.GetSingle("color0_const_rgb", 0), fields.GetSingle("color0_const_rgb", 1), fields.GetSingle("color0_const_rgb", 2)];
        }

        Directory.CreateDirectory(cache.Effects);
        File.WriteAllText(FieldHazeManifest.Path(cache.Effects), JsonSerializer.Serialize(new FieldHazeManifest(color, [.. levels])));
    }

    static StructView Emitter(TotkEffectFile file, string set) =>
        new(PtclLayouts.For(PtclVersion.TotK_NintendoWareVfx2_15_3_1).Emitter, file.EmitterData(set, file.EmitterNames(set).First()));

    static FieldHazeLevel Level(IRomAccess rom, TotkEffectFile file, StructView fields, TotkEffectMesh mesh)
    {
        // One sprite is emitted every interval plus one frames and lives its lifespan.
        float particles = fields.GetUInt32("particle_lifespan") / (fields.GetInt32("emit_interval") + 1f);
        float radius = fields.GetSingle("shape_radius_xyz", 0);
        float area = mesh.Extent(0) * fields.GetSingle("particle_scale_xyz", 0) * mesh.Extent(1) * fields.GetSingle("particle_scale_xyz", 1);

        // The fade in over the camera distances of 0x8A0 to 0x8A4, and out over 0x8A8 to 0x8AC where that begins after the fade in is over; the programs
        // of the outermost level have no fade out, and leave the field holding another number.
        float inStart = fields.GetSingle("unverified_8A0"), inEnd = fields.GetSingle("unverified_8A4");
        float outStart = fields.GetSingle("unverified_8A8"), outEnd = fields.GetSingle("unverified_8AC");
        bool fadesOut = outStart >= inEnd && outEnd > outStart;
        return new FieldHazeLevel(fields.GetSingle("emitter_trans_xyz", 2), radius, particles, area, Opacity(rom, file, fields),
            inStart, inEnd, fadesOut ? outStart : 0f, fadesOut ? outEnd : 0f, fields.GetSingle("unverified_8C4"));
    }

    // A sprite's opacity is clamp(4 * (the product of its two textures' alphas - the first track's value)) times the second track, and the engine scales
    // each by a custom value it sets, which is taken as one. The mean over the sprite's life and its texels is the track's mean times the texture term.
    static float Opacity(IRomAccess rom, TotkEffectFile file, StructView fields)
    {
        float threshold = fields.GetSingle("kf_alpha0", 0);
        float[]? first = Texels(rom, file, fields.GetUInt64("tex_slot1_guid"));
        float[]? second = Texels(rom, file, fields.GetUInt64("tex_slot2_guid"));
        var random = new Random(TexelSeed);
        float coverage = 0f;
        for (int pair = 0; pair < TexelPairs; pair++)
        {
            float a = first is null ? 1f : first[random.Next(first.Length)];
            float b = second is null ? 1f : second[random.Next(second.Length)];
            coverage += Math.Clamp(4f * ((a * b) - threshold), 0f, 1f);
        }

        float life = 0f;
        for (int sample = 0; sample < LifeSamples; sample++)
            life += Track(fields, "alpha1", (sample + 0.5f) / LifeSamples);
        return coverage / TexelPairs * (life / LifeSamples);
    }

    static float[]? Texels(IRomAccess rom, TotkEffectFile file, ulong guid) =>
        guid == ulong.MaxValue ? null : TotkEffectTexture.Alphas(rom, file.TextureName((uint)guid));

    static float Track(StructView fields, string track, float time)
    {
        string keys = "kf_" + track;
        int count = (int)fields.GetUInt32(track + "_key_count");

        if (fields.GetByte(track + "_mode") != KeyedMode && count <= 1)
            return fields.GetSingle(track + "_const");

        float[] values = new float[count], times = new float[count];
        for (int key = 0; key < count; key++)
        {
            values[key] = fields.GetSingle(keys, key * 4);
            times[key] = fields.GetSingle(keys, (key * 4) + 3);
        }

        if (time <= times[0])
            return values[0];
        for (int key = 1; key < count; key++)
            if (time <= times[key])
                return values[key - 1] + ((values[key] - values[key - 1]) * (time - times[key - 1]) / MathF.Max(times[key] - times[key - 1], 1e-6f));
        return values[count - 1];
    }
}
