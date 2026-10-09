using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>
/// The game's own programs for a crbin mesh (a cave, a sky island, an edit part), linked as they are. The pages are already what its vertex stage
/// reads (28 bytes a vertex, bound as two <c>vec4</c>s at locations 0 and 1), so the host feeds them with <c>cave_ChunkDynamicDataUBO</c>
/// (<see cref="ChunkBinding"/>), the material table <c>cave_MaterialPaletteUBO</c> (<see cref="PaletteBinding"/>) and the placement
/// <c>cave_CaveInstanceDynamicDataUBO</c> (<see cref="InstanceBinding"/>). The fragment stage reads the terrain's material arrays on its units.
/// </summary>
public sealed class CaveShading : IDisposable
{
    public const uint ChunkBinding = 3, InstanceBinding = 12, PaletteBinding = 13;

    readonly GL _gl;
    readonly string _shadersDir;
    MaterialBlock? _material;

    internal CaveShading(GL gl, string shadersDir)
    {
        _gl = gl;
        _shadersDir = shadersDir;
    }

    public bool Available =>
        File.Exists(Path.Combine(_shadersDir, "cave_prog2_extracted.frag"))
        && File.Exists(Path.Combine(_shadersDir, "cave_prog1_extracted.frag"));

    public uint LinkGBufferProgram() => Link(2);

    public uint LinkDepthProgram() => Link(1);

    uint Link(int program)
    {
        string vert = TotkGlsl.Clean(File.ReadAllText(Path.Combine(_shadersDir, $"cave_prog{program}_extracted.vert")));
        string frag = TotkGlsl.Clean(File.ReadAllText(Path.Combine(_shadersDir, $"cave_prog{program}_extracted.frag")));
        return GLProgramBuilder.Build(_gl, vert, frag, $"cave_prog{program}");
    }

    internal MaterialBlock Material => _material ??= MaterialBlock.FromFile(_gl, Path.Combine(_shadersDir, "cave_gsys_material.bin"));

    public void Dispose() => _material?.Dispose();
}
