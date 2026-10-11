
namespace WildRenderingSharp.Assets.Textures;

/// <summary>One resolved texture binding on a shape: the shader unit, the sampler key it was bound through (e.g. "_a0"), and the texture.</summary>
internal readonly record struct ShapeSampler(int Unit, string Key, LoadedTexture Texture);
