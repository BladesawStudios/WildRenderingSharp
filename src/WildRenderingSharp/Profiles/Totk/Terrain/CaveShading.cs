using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>
/// The game's own programs for a crbin mesh (a cave, a sky island, an edit part, a well), linked as they are, vertex stage included.
/// </summary>
/// <remarks>
/// <para>
/// A crbin's page files are already what the game's vertex stage reads: 28 bytes a vertex, a patch word, a parent block and a self block, bound as
/// two <c>vec4</c>s of raw bits (<c>cave_aVertexData0</c> at location 0, <c>cave_aVertexData1</c> at 1). So the host feeds the pages, an index range
/// per stream, and three uniform blocks:
/// </para>
/// <list type="bullet">
/// <item><description><c>cave_ChunkDynamicDataUBO</c> (<see cref="ChunkBinding"/>), one per node: where its quantised positions start and how far a
/// step is, the node's first material, and the morph that blends one level of detail into the next; all zero draws the node's own level.</description></item>
/// <item><description><c>cave_MaterialPaletteUBO</c> (<see cref="PaletteBinding"/>): the crbin's material table, 32 bytes an entry, indexed by a
/// vertex's three slots from the node's first material.</description></item>
/// <item><description><c>cave_CaveInstanceDynamicDataUBO</c> (<see cref="InstanceBinding"/>): the placement, three rows.</description></item>
/// </list>
/// <para>
/// The fragment stage's <c>cTexture0</c> and <c>cTexture1</c> are the terrain's <c>MaterialAlb</c> and <c>MaterialCmb</c> arrays on the same units
/// (<see cref="TerrainShading.AlbedoArrayUnit"/> and <see cref="TerrainShading.CombinedArrayUnit"/>).
/// </para>
/// </remarks>
public sealed class CaveShading : IDisposable
{
    public const uint ChunkBinding = 3, InstanceBinding = 12, PaletteBinding = 13;

    readonly GL _gl;
    readonly string _shadersDir;
    uint _materialBuffer;

    internal CaveShading(GL gl, string shadersDir)
    {
        _gl = gl;
        _shadersDir = shadersDir;
    }

    /// <summary>Whether the preparer has exported the cave programs into the cache.</summary>
    public bool Available =>
        File.Exists(Path.Combine(_shadersDir, "cave_prog2_extracted.frag"))
        && File.Exists(Path.Combine(_shadersDir, "cave_prog1_extracted.frag"));

    /// <summary>The G-buffer program: the outputs and soft edge of the terrain's.</summary>
    public uint LinkGBufferProgram() => Link(2);

    /// <summary>The depth-only program, for the shadow cascades.</summary>
    public uint LinkDepthProgram() => Link(1);

    uint Link(int program)
    {
        string vert = GlslSanitizer.Clean(File.ReadAllText(Path.Combine(_shadersDir, $"cave_prog{program}_extracted.vert")));
        string frag = GlslSanitizer.Clean(File.ReadAllText(Path.Combine(_shadersDir, $"cave_prog{program}_extracted.frag")));
        return GLProgramBuilder.Build(_gl, vert, frag, $"cave_prog{program}");
    }

    /// <summary>The cave model's default <c>gsys_material</c>, bound at the material binding while a mesh draws.</summary>
    public uint MaterialBuffer
    {
        get
        {
            if (_materialBuffer == 0)
            {
                string path = Path.Combine(_shadersDir, "cave_gsys_material.bin");
                _materialBuffer = GLBuffer.CreatePaddedUniformBuffer(_gl, File.Exists(path) ? File.ReadAllBytes(path) : []);
            }
            return _materialBuffer;
        }
    }

    public void Dispose()
    {
        if (_materialBuffer != 0)
            _gl.DeleteBuffer(_materialBuffer);
    }
}
