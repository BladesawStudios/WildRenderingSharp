using System.Numerics;

namespace WildRenderingSharp.Assets;

/// <summary>One placed actor's baked lighting: its tile's atlas, and each material's region of it by material index.</summary>
public sealed record BakeActor(LoadedTexture Atlas, Vector4[] StByMaterial, IReadOnlyDictionary<string, int> MaterialIndexByName);
