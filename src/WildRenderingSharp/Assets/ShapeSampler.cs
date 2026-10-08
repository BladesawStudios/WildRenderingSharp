
namespace WildRenderingSharp.Assets;

/// <summary>
/// One resolved texture binding on a shape: the shader unit, the sampler key it was bound through (e.g. "_a0"), and the
/// texture. The key is carried because a texture pattern anim re-points a sampler by key.
/// </summary>
public readonly record struct ShapeSampler(int Unit, string Key, LoadedTexture Texture);
