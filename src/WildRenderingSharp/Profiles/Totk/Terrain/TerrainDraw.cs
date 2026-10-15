using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>What a host's terrain draw gets: the GL context, the camera position, the cascade being drawn (-1 for none) and the region it covers.</summary>
/// <remarks>
/// A shadow draw also says which way the sun is and how wide the light's view is. Whatever lies toward the sun along the region's
/// width can shadow it, however far off, so a host should not cut its casters to the region's own sphere: <see cref="TowardSun"/>
/// is a unit direction and <see cref="LightRadius"/> is the half width of the light's view, both zero outside a shadow draw.
/// </remarks>
public readonly record struct TerrainDraw(
    GL Gl, Vector3 CameraPosition, int Cascade, Vector4 Region, Vector3 TowardSun = default, float LightRadius = 0f);
