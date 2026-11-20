using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Graph.Pipeline.Frame;

/// <summary>Where a frame's shared inputs live in the content stores.</summary>
internal sealed record AssetDirectories(IContentFiles Decompiled, IContentFiles DeferredMaterials, IContentFiles SystemTextures, IContentFiles Effects);
