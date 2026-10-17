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
            levels.Add(Level(fields, TotkEffectMesh.Read(resource, modelIds.ToList().IndexOf((uint)fields.GetUInt64("g3d_primitive_idx")))));
            color = [fields.GetSingle("color0_const_rgb", 0), fields.GetSingle("color0_const_rgb", 1), fields.GetSingle("color0_const_rgb", 2)];
        }

        Directory.CreateDirectory(cache.Effects);
        File.WriteAllText(FieldHazeManifest.Path(cache.Effects), JsonSerializer.Serialize(new FieldHazeManifest(color, [.. levels])));
    }

    static StructView Emitter(TotkEffectFile file, string set) =>
        new(PtclLayouts.For(PtclVersion.TotK_NintendoWareVfx2_15_3_1).Emitter, file.EmitterData(set, file.EmitterNames(set).First()));

    static FieldHazeLevel Level(StructView fields, TotkEffectMesh mesh)
    {
        // One sprite is emitted every interval plus one frames and lives its lifespan.
        float particles = fields.GetUInt32("particle_lifespan") / (fields.GetInt32("emit_interval") + 1f);
        float radius = fields.GetSingle("shape_radius_xyz", 0);
        float area = mesh.Extent(0) * fields.GetSingle("particle_scale_xyz", 0) * mesh.Extent(1) * fields.GetSingle("particle_scale_xyz", 1);
        return new FieldHazeLevel(fields.GetSingle("emitter_trans_xyz", 2), radius, particles, area, MeanAlpha(fields));
    }

    // The opacity is the product of two tracks, each a constant or a table of keys (a value, then its time in the life).
    static float MeanAlpha(StructView fields)
    {
        float sum = 0f;
        for (int sample = 0; sample < LifeSamples; sample++)
        {
            float time = (sample + 0.5f) / LifeSamples;
            sum += Track(fields, "alpha0", time) * Track(fields, "alpha1", time);
        }
        return sum / LifeSamples;
    }

    static float Track(StructView fields, string track, float time)
    {
        string keys = "kf_" + track;
        int count = (int)fields.GetUInt32(track + "_key_count");

        // TODO: the haze emitters leave their first opacity track in constant mode yet author a table of keys for it, and the constant would give about
        // 40% less haze at the distances a screenshot of the game fits. The table is used where there is one; reading the engine's own choice would settle it.
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
