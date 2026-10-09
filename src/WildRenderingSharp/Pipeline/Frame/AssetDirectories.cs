namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Where a frame's shared inputs live on disk.</summary>
internal sealed record AssetDirectories(string Decompiled, string DeferredMaterials, string SystemTextures);
