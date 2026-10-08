using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>One placed actor for a frame: its placement and, if a clip is applied, its posed bones.</summary>
public sealed record ActorRenderInput(LoadedModel Model, Vector4[] ModelMatrixRows, Matrix4x4[]? BoneWorldMatrices = null);
