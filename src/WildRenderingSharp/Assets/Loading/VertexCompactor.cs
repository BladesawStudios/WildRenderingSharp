using System.Numerics;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Assets.Loading;

/// <summary>Drops the vertex attributes no program of a shape reads, repacking the buffer around the ones it keeps.</summary>
static class VertexCompactor
{
    public static readonly string[] BlendAttributes = ["aBlendWeight0", "aBlendWeight1", "aBlendIndex0", "aBlendIndex1"];

    // The attributes a shape's buffer must carry: whatever its programs read, position, and for a skinned shape the blend
    // attributes the pass-ID stamp skins with, less those when the shared constant vertex replaces them.
    public static HashSet<string> UsedAttributes(IReadOnlyList<VertexLayoutEntry> layout, int skinCount, bool constantSkin,
        IEnumerable<HashSet<int>> activeLocationsPerProgram)
    {
        var used = new HashSet<string> { "aPosition" };
        foreach (var active in activeLocationsPerProgram)
            foreach (var entry in layout)
                if (active.Contains(entry.Location))
                    used.Add(entry.Name);
        if (skinCount >= 1)
            used.UnionWith(BlendAttributes);
        if (constantSkin)
            used.ExceptWith(BlendAttributes);
        return used;
    }

    public static (List<VertexLayoutEntry> Layout, int Stride, byte[] Bytes) Compact(List<VertexLayoutEntry> layout, int stride, byte[] bytes,
        HashSet<string> used)
    {
        var kept = layout.Where(e => used.Contains(e.Name)).OrderBy(e => e.Offset).ToList();
        var packed = new List<VertexLayoutEntry>(kept.Count);
        int newStride = 0;
        foreach (var e in kept)
        {
            packed.Add(new VertexLayoutEntry { Name = e.Name, Location = e.Location, Offset = newStride, Components = e.Components });
            newStride += e.Components * sizeof(float);
        }
        if (newStride == stride)
            return (layout, stride, bytes);

        int count = bytes.Length / stride;
        var result = new byte[count * newStride];
        for (int v = 0; v < count; v++)
            for (int a = 0; a < kept.Count; a++)
                Buffer.BlockCopy(bytes, v * stride + kept[a].Offset, result, v * newStride + packed[a].Offset, kept[a].Components * sizeof(float));
        return (packed, newStride, result);
    }

    // Whether every smooth-skinned bone sits at its bind pose, so one shared vertex can stand in for the blend attributes.
    public static bool SmoothPaletteIsIdentity(SkeletonManifest? skeleton)
    {
        if (skeleton is null)
            return true;

        Matrix4x4[] palette = InstanceBatch.BindPalette(skeleton);
        int smooth = Math.Min(skeleton.InverseModelMatricesAsMatrices().Length, palette.Length);
        for (int i = 0; i < smooth; i++)
            if (!IsIdentity(palette[i]))
                return false;
        return true;
    }

    static bool IsIdentity(Matrix4x4 m)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (MathF.Abs(m[r, c] - Matrix4x4.Identity[r, c]) > 1e-3f)
                    return false;
        return true;
    }
}
