namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Where a frame's shared inputs live on disk.</summary>
public sealed record AssetDirectories(string Decompiled, string DeferredMaterials, string SystemTextures);
