namespace KingRenderingSharp.Content.Packs;

/// <summary>The fixed 192-byte interleaved vertex every prepared shape is stored in: where each attribute a program may read sits, and how wide it is.</summary>
static class KingVertexLayout
{
    public const int Stride = 192;

    public static readonly (int Location, int Offset, int Components, string Name)[] Attributes =
    [
        (0, 0, 4, "aPosition"),
        (1, 16, 4, "aNormal"),
        (2, 32, 4, "aTangent"),
        (3, 48, 4, "aU254"),
        (4, 64, 4, "aBlendWeight0"),
        (5, 80, 4, "aBlendWeight1"),
        (6, 96, 4, "aBlendIndex0"),
        (7, 112, 4, "aBlendIndex1"),
        (8, 128, 2, "aTexCoord0"),
        (9, 136, 2, "aTexCoord1"),
        (10, 144, 2, "aTexCoord2"),
        (11, 152, 2, "aTexCoord3"),
        // The bare name, not aColor0: every program of the corpus declares aColor.
        (12, 160, 4, "aColor"),
        (13, 176, 4, "aColor1"),
    ];

    // For each used location in offset order "Nf" for its floats and "Nx" padding over the bytes a program does not use, so the format always sums to the stride.
    public static string Format(IReadOnlySet<int> used)
    {
        List<string> parts = [];
        int cursor = 0;
        foreach (var (location, offset, components, _) in Attributes.OrderBy(a => a.Offset))
        {
            if (!used.Contains(location)) continue;
            if (offset > cursor) parts.Add($"{offset - cursor}x");
            parts.Add($"{components}f");
            cursor = offset + components * 4;
        }

        if (cursor < Stride) parts.Add($"{Stride - cursor}x");
        return string.Join(' ', parts);
    }
}
