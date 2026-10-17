using PtclSharp.Layout;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>
/// Puts the constants of an emitter's colour and alpha tracks where its programs read them. A track in constant mode keeps its value in a separate field, and the
/// engine copies it into the first key of the track's table when the emitter is created.
/// </summary>
internal static class TotkEmitterConstants
{
    // Each track's mode field, its constant, its key table, and how many components it has.
    static readonly (string Mode, string Constant, string Keys, int Components)[] Tracks =
    [
        ("color0_mode", "color0_const_rgb", "kf_color0", 3),
        ("alpha0_mode", "alpha0_const", "kf_alpha0", 1),
        ("color1_mode", "color1_const_rgb", "kf_color1", 3),
        ("alpha1_mode", "alpha1_const", "kf_alpha1", 1),
    ];

    // The constant mode, in which the track holds one value and no keys.
    const int ConstantMode = 0;

    public static void Apply(StructView fields)
    {
        foreach (var (mode, constant, keys, components) in Tracks)
        {
            if (fields.GetByte(mode) != ConstantMode)
                continue;
            for (int component = 0; component < components; component++)
                fields.SetSingle(keys, fields.GetSingle(constant, component), component);
        }
    }
}
