using System.Numerics;
using KingRenderingSharp.Content.Assets;

namespace KingRenderingSharp.Graph.Pipeline;

/// <summary>One placed actor for a frame: its placement and, if a clip is applied, its posed bones.</summary>
public sealed record ActorRenderInput(LoadedModel Model, Vector4[] ModelMatrixRows, Matrix4x4[]? BoneWorldMatrices = null);
