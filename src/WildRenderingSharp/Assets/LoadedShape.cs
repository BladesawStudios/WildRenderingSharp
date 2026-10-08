
namespace WildRenderingSharp.Assets;

/// <summary>One shape ready to draw: GL objects built from a <see cref="ShapeManifestEntry"/> by <see cref="ModelLoader"/>.</summary>
public sealed class LoadedShape
{
    public required string Name { get; init; }
    public required string Material { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    public required bool AlphaTest { get; init; }
    public required bool Blend { get; init; }
    public required RenderState RenderState { get; init; }

    public required int VertexSkinCount { get; init; }

    public required uint VertexBuffer { get; init; }
    public required uint IndexBuffer { get; init; }
    public required int IndexCount { get; init; }

    public IReadOnlyList<(int FirstIndex, int Count)> Lods { get; init; } = [];

    public (int FirstIndex, int Count) Lod(int lod) =>
        Lods.Count == 0 ? (0, IndexCount) : Lods[Math.Clamp(lod, 0, Lods.Count - 1)];

    public required uint GBufferProgram { get; init; }
    public uint GBufferVao { get; internal set; }
    public required IReadOnlyList<ShapeSampler> GBufferSamplers { get; init; }

    public uint ZOnlyProgram { get; init; }
    public uint ZOnlyVao { get; internal set; }
    public IReadOnlyList<ShapeSampler> ZOnlySamplers { get; init; } = [];

    public uint ForwardProgram { get; init; }
    public uint ForwardVao { get; internal set; }
    public IReadOnlyList<ShapeSampler> ForwardSamplers { get; init; } = [];

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

    public required uint MaterialBuffer { get; init; }

    public required byte[] MaterialBytes { get; init; }

    public MaterialParamLayout? MaterialParams { get; init; }

    public bool MaterialIsPatched { get; set; }

    /// <summary>Whether the instance's baked lighting has a region for this material; one that does not keeps its own bake0.</summary>
    public bool HasBakeRegion { get; set; } = true;

    public uint PassIdVao { get; internal set; }

    public bool HasZOnly => ZOnlyVao != 0;

    public bool Hidden { get; init; }

    public bool CastsShadow { get; init; } = true;

    public bool ReadsSceneColor { get; init; }
    public bool HasForward => ForwardVao != 0;

    public bool Enabled { get; set; } = true;

    public bool ForceForward { get; set; }
}
