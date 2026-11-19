using System.Text.Json;
using System.Text.Json.Serialization;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Animation.Clips;

/// <summary>
/// A complete texture pattern animation, which can swap textures on several
/// materials via the animation system.
/// Loaded from <c>&lt;Model&gt;.&lt;AnimName&gt;.texpat.json</c> in the cache
/// </summary>
public sealed class TexturePatternAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    
    // All the textures the animation may select.
    [JsonPropertyName("textures")] public List<TexturePatternTextureEntry> Textures { get; set; } = [];
    [JsonPropertyName("materials")] public List<TexturePatternMaterialEntry> Materials { get; set; } = [];




    public SamplerBinding? BindingFor(int textureIndex, string samplerKey)
    {
        if (textureIndex < 0 || textureIndex >= Textures.Count)
            return null;
        var t = Textures[textureIndex];
        if (string.IsNullOrEmpty(t.File))
            return null;
        return new SamplerBinding
        {
            Unit = -1, // the unit belongs to the shape's own sampler list, not to the anim
            Key = samplerKey,
            Assigned = samplerKey,
            Texture = t.Texture,
            File = t.File,
            Format = t.Format,
            Width = t.Width,
            Height = t.Height,
            CompSelect = t.CompSelect,
        };
    }
}
