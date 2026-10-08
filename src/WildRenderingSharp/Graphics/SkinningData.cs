using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Graphics;

/// <summary>One actor's placement and pose.</summary>
public readonly record struct SkinningData(Vector4[] PlacementRows, SkeletonManifest? Skeleton, Matrix4x4[]? BoneWorld);
