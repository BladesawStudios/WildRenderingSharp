using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Loading;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Assets.Textures;


namespace WildRenderingSharp.Assets;

/// <summary>One shape ready to draw: GL objects built from a <see cref="ShapeManifestEntry"/> by <see cref="ModelLoader"/>.</summary>
public sealed class LoadedShape
{
    public required string Name { get; init; }
    public required string Material { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    public required bool AlphaTest { get; init; }
    public required bool Blend { get; init; }
    internal RenderState RenderState { get; init; } = null!;

    public required int VertexSkinCount { get; init; }

    // The buffers the shape was built into, with the layout of the vertices in them.
    internal ShapeBuffers Buffers { get; init; } = null!;

    // What the merged draw holds of the shape, made the first time it is drawn that way.
    internal IShapeCopy? Merged { get; set; }

    // The shape's own buffers and vertex arrays can be given up once the merged draw holds a copy of the geometry, which is most of what a scene's shapes
    // weigh. Anything that reads them after that finds them made again from the copy, so only a shape drawn the ordinary way ever has them back.
    uint _vertexBuffer, _indexBuffer, _gBufferVao, _zOnlyVao, _forwardVao, _passIdVao;

    internal bool Demoted { get; private set; }

    void Revive()
    {
        if (!Demoted)
            return;
        Demoted = false;
        Merged!.Restore(this);
    }

    internal void Demote(GL gl)
    {
        if (Demoted || Merged is null)
            return;
        DeleteGl(gl);
        Demoted = true;
    }

    internal void Rebuilt(uint vertexBuffer, uint indexBuffer, uint gBufferVao, uint zOnlyVao, uint forwardVao, uint passIdVao)
    {
        (_vertexBuffer, _indexBuffer, _gBufferVao, _zOnlyVao, _forwardVao, _passIdVao) = (vertexBuffer, indexBuffer, gBufferVao, zOnlyVao, forwardVao, passIdVao);
        Gpu.GpuMemory.Shape(Buffers.VertexBytes + Buffers.IndexBytes);
    }

    // Deletes whatever of the shape's own GL objects exists, without making any of it first.
    internal void DeleteGl(GL gl)
    {
        if (_gBufferVao != 0) gl.DeleteVertexArray(_gBufferVao);
        if (_zOnlyVao != 0) gl.DeleteVertexArray(_zOnlyVao);
        if (_forwardVao != 0) gl.DeleteVertexArray(_forwardVao);
        if (_passIdVao != 0) gl.DeleteVertexArray(_passIdVao);
        if (_vertexBuffer != 0) gl.DeleteBuffer(_vertexBuffer);
        if (_indexBuffer != 0) gl.DeleteBuffer(_indexBuffer);
        if (_vertexBuffer != 0 && Buffers is not null)
            Gpu.GpuMemory.Shape(-(Buffers.VertexBytes + Buffers.IndexBytes));
        (_vertexBuffer, _indexBuffer, _gBufferVao, _zOnlyVao, _forwardVao, _passIdVao) = (0, 0, 0, 0, 0, 0);
    }

    public required uint VertexBuffer { get { Revive(); return _vertexBuffer; } init => _vertexBuffer = value; }
    public required uint IndexBuffer { get { Revive(); return _indexBuffer; } init => _indexBuffer = value; }
    public required int IndexCount { get; init; }

    public IReadOnlyList<(int FirstIndex, int Count)> Lods { get; init; } = [];

    public (int FirstIndex, int Count) Lod(int lod) =>
        Lods.Count == 0 ? (0, IndexCount) : Lods[Math.Clamp(lod, 0, Lods.Count - 1)];

    public required uint GBufferProgram { get; init; }
    public uint GBufferVao { get { Revive(); return _gBufferVao; } internal set => _gBufferVao = value; }
    internal IReadOnlyList<ShapeSampler> GBufferSamplers { get; init; } = [];

    public uint ZOnlyProgram { get; init; }
    public uint ZOnlyVao { get { Revive(); return _zOnlyVao; } internal set => _zOnlyVao = value; }
    internal IReadOnlyList<ShapeSampler> ZOnlySamplers { get; init; } = [];

    public uint ForwardProgram { get; init; }
    public uint ForwardVao { get { Revive(); return _forwardVao; } internal set => _forwardVao = value; }
    internal IReadOnlyList<ShapeSampler> ForwardSamplers { get; init; } = [];

    public string ForwardShaderName { get; init; } = "";

    public string GBufferShaderName { get; init; } = "";

    public string ZOnlyShaderName { get; init; } = "";

    public uint InstancedGBufferProgram { get; internal set; }
    public uint InstancedZOnlyProgram { get; internal set; }
    public uint InstancedForwardProgram { get; internal set; }
    internal bool InstancedProgramsLinked { get; set; }

    public uint? DebugForwardProgram { get; set; }

    public int DebugStepTarget { get; set; } = -1;

    public uint? DebugGBufferProgram { get; set; }

    public int DebugGBufferStepTarget { get; set; } = -1;

    public IReadOnlyDictionary<string, LoadedTexture>? SamplerOverrides { get; set; }

    internal MaterialBlock MaterialBlock { get; init; } = null!;

    internal MaterialParamLayout? MaterialParams { get; init; }

    // Whether the instance's baked lighting has a region for this material; one that does not keeps its own bake0.
    public bool HasBakeRegion { get; set; } = true;

    public uint PassIdVao { get { Revive(); return _passIdVao; } internal set => _passIdVao = value; }

    // A vertex array is made for each program the shape has, so these ask the program, which does not make the shape's buffers first.
    public bool HasZOnly => ZOnlyProgram != 0;

    public bool Hidden { get; init; }

    public bool CastsShadow { get; init; } = true;

    public bool ReadsSceneColor { get; init; }
    public bool HasForward => ForwardProgram != 0;

    public bool Enabled { get; set; } = true;

    public bool ForceForward { get; set; }
}
