using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Content.Assets.Materials;
using KingRenderingSharp.Gpu;
using KingRenderingSharp.Games.Totk.Shaders;
using KingRenderingSharp.Content.Shaders;

namespace KingRenderingSharp.Games.Totk.Terrain;

/// <summary>
/// The game's own programs for a crbin mesh (a cave, a sky island, an edit part), fed by the host through the cave UBOs and the terrain's
/// material arrays.
/// </summary>
public sealed class CaveShading : IDisposable
{
    public const uint ChunkBinding = 3, InstanceBinding = 12, PaletteBinding = 13;

    readonly GpuApi _gl;
    readonly IContentFiles _shaders;
    MaterialBlock? _material;

    internal CaveShading(GpuApi gl, IContentFiles shaders)
    {
        _gl = gl;
        _shaders = shaders;
    }

    public bool Available =>
        _shaders.Exists("cave_prog2_extracted.frag") && _shaders.Exists("cave_prog1_extracted.frag");

    public uint LinkGBufferProgram() => Link(2);

    public uint LinkDepthProgram() => Link(1);

    uint Link(int program)
    {
        var (vert, frag) = ExtractedProgramSource.Read(_shaders, $"cave_prog{program}");
        return GLProgramBuilder.Build(_gl, vert, frag, $"cave_prog{program}");
    }

    internal MaterialBlock Material => _material ??= MaterialBlock.From(_gl, _shaders, "cave_gsys_material.bin");

    public void Dispose() => _material?.Dispose();
}
