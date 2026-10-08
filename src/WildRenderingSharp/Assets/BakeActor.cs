using System.Numerics;

namespace WildRenderingSharp.Assets;

/// <summary>One placed actor's baked lighting: its tile's atlas, and each material's region of it by material index.</summary>
/// <param name="Atlas">The atlas texture (BC4, its whole mip chain).</param>
/// <param name="StByMaterial">Indexed by the model's material index: (scale x, scale y, offset x, offset y) into the atlas; zero where a material has no region.</param>
/// <param name="MaterialIndexByName">The model's material index for each material name the bake lists.</param>
public sealed record BakeActor(LoadedTexture Atlas, Vector4[] StByMaterial, IReadOnlyDictionary<string, int> MaterialIndexByName);
