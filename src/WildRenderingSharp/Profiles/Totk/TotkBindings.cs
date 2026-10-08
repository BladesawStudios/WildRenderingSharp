namespace WildRenderingSharp.Profiles.Totk;

/// <summary>Where TotK's shaders read each uniform block, as recovered from the compiled programs.</summary>
public static class TotkBindings
{
    public const uint Support = 0;
    public const uint Camera = 1;
    public const uint Bones = 2;
    public const uint TerrainWaterStamp = 3;
    public const uint ShapeMatrix = 4;
    public const uint HdrComposeParams = 4;
    public const uint Environment = 6;
    public const uint Material = 8;
    public const uint SceneMaterial = 10;

    /// <summary>Where blocks the decompiler left with a negative binding are moved to.</summary>
    public const uint Orphan = 30;
}
