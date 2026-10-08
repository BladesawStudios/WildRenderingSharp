using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Graphics;

/// <summary>One actor's placement and pose.</summary>
/// <param name="PlacementRows">Model to the renderer's world, three rows.</param>
/// <param name="Skeleton">Null for a model with no bones.</param>
/// <param name="BoneWorld">Posed bone matrices, or null for the bind pose.</param>
public readonly record struct SkinningData(Vector4[] PlacementRows, SkeletonManifest? Skeleton, Matrix4x4[]? BoneWorld);
