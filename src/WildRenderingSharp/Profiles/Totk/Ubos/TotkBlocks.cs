using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>The smaller uniform blocks TotK's shaders read, with the one field each of them has filled.</summary>
static class TotkBlocks
{
    public static readonly UboSpec Bones = BonePalette.SpecAt(TotkBindings.Bones);

    public static readonly UboSpec ShapeMatrix = new("ShpMtx", TotkBindings.ShapeMatrix, 256);
    public static readonly UboMatrix ShapeModel = new(0, 3);

    public static readonly UboSpec HdrComposeParams = new("cContext", TotkBindings.HdrComposeParams, 256);

    public static readonly UboSpec WaterStamp = new("_WrsStamp", TotkBindings.TerrainWaterStamp, 32);

    public static readonly UboSpec Orphan = new("", TotkBindings.Orphan, 65536);
}
