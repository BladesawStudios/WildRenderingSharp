using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>What a host's terrain draw gets: the GL context, the camera position, the cascade being drawn (-1 for none) and the region it covers.</summary>
public readonly record struct TerrainDraw(GL Gl, Vector3 CameraPosition, int Cascade, Vector4 Region);
