using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The shapes of the game's gloom renderer that stand in the world as a box of their own; the Depths' altitude gloom has no box.</summary>
public enum GloomShape { Sphere = 0, Box = 1, Cylinder = 2 }

/// <summary>One gloom volume as the game's renderer draws it: where its unit box stands, and the constants its area type gives the shape pass.</summary>
/// <remarks>
/// <see cref="UnitToWorld"/> takes the shape's unit box (centred, a unit on a side) to the world, row-vector like every System.Numerics matrix.
/// A sphere or box has <see cref="Density"/> in X alone; a cylinder takes its top density in X and its bottom in Y, thinning toward its axis by
/// <see cref="InnerRatio"/> as it rises. Only a cylinder scales its noise. A cancelling volume thins the gloom under it and clears it where it is dense.
/// </remarks>
public readonly record struct GloomVolume(
    GloomShape Shape, Matrix4x4 UnitToWorld, Vector2 Density, Vector2 InnerRatio, float NoiseScale, bool Cancels);
