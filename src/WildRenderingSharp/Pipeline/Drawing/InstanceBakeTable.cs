using System.Numerics;
using WildRenderingSharp.Assets.Baking;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>Which baked-lighting atlas each instance uses, and the table of per-material atlas rectangles their shaders look up.</summary>
sealed record InstanceBakeTable(int[] AtlasOfInstance, IReadOnlyList<LoadedTexture> Atlases, Vector4[] Table, Vector4[] Row8)
{
    // Entries left empty at the head of the table: binding 0 is also where a water program writes a per-pixel value (entry 28), so
    // the table starts past it.
    const int Head = 64;

    public static InstanceBakeTable Build(int count, IReadOnlyList<BakeActor?> perInstance)
    {
        var atlases = new List<LoadedTexture>();
        var atlasOf = new int[count];
        var row8 = new Vector4[count];
        var table = new List<Vector4>(new Vector4[Head]);
        for (int i = 0; i < count; i++)
        {
            atlasOf[i] = -1;
            if (i >= perInstance.Count || perInstance[i] is not { } bake || bake.StByMaterial.Length == 0)
                continue;

            int atlas = atlases.IndexOf(bake.Atlas);
            if (atlas < 0)
            {
                atlas = atlases.Count;
                atlases.Add(bake.Atlas);
            }
            atlasOf[i] = atlas;
            row8[i].Y = BitConverter.Int32BitsToSingle(0x40000000 | (table.Count & 0xFFFFF));
            table.AddRange(bake.StByMaterial);
        }
        return new InstanceBakeTable(atlasOf, atlases, table.ToArray(), row8);
    }
}
