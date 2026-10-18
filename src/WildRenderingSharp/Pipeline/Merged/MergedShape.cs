using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>What the merged draw holds of one shape: where its geometry, material and texture handles lie, and the programs it draws with.</summary>
internal sealed class MergedShape(MergedDrawing owner, GeometrySlice geometry, int materialBase) : IShapeCopy
{
    public MergedDrawing Owner { get; } = owner;

    public GeometrySlice Geometry { get; } = geometry;

    // Whether a texture of the shape is one the host swaps for another at will, which a handle taken once would not follow.
    public bool Excluded { get; init; }

    public void Release(LoadedShape shape) => Owner.Release(shape, this);

    public void Restore(LoadedShape shape) => Owner.Restore(shape, this);

    // The first vec4 of the shape's material in the merged buffer.
    public int MaterialBase { get; } = materialBase;

    // The first handle of the shape's textures for each program it draws with, or -1 when it has none.
    public int GBufferTextures { get; set; } = -1;

    public int ZOnlyTextures { get; set; } = -1;

    // The merged program of each variant, 0 where it has none: null until asked for.
    public uint? GBufferProgram { get; set; }

    public uint? ZOnlyProgram { get; set; }
}
