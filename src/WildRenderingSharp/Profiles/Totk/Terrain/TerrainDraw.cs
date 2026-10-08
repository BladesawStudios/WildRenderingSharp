using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <param name="CameraYUp">The camera's position in the game's Y-up world.</param>
/// <param name="Cascade">The shadow cascade being drawn, or -1 for the G-buffer.</param>
/// <param name="Region">The region the cascade covers (Y-up centre, radius) - for culling tiles outside it.</param>
public readonly record struct TerrainDraw(GL Gl, Vector3 CameraYUp, int Cascade, Vector4 Region);
