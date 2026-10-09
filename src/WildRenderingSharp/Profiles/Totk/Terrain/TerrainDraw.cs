using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

public readonly record struct TerrainDraw(GL Gl, Vector3 CameraPosition, int Cascade, Vector4 Region);
