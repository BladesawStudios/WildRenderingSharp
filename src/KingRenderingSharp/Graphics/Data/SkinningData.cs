using System.Numerics;
using KingRenderingSharp.Assets.Manifests;

namespace KingRenderingSharp.Graphics.Data;

/// <summary>One actor's placement and pose.</summary>
internal readonly record struct SkinningData(Vector4[] PlacementRows, SkeletonManifest? Skeleton, Matrix4x4[]? BoneWorld);
