using System;

namespace WildRenderingSharp.Cloth.Model;

public sealed class HclBufferDefinition
{
    public string MeshName { get; set; } = string.Empty;
    public string BufferName { get; set; } = string.Empty;
    public uint Type { get; set; }
    public uint SubType { get; set; }
    public uint NumVertices { get; set; }
    public uint NumTriangles { get; set; }
}

public sealed class HclTransformSetDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; }
    public uint NumTransforms { get; set; }
}
