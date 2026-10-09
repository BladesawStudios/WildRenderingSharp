using System.Text.RegularExpressions;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>
/// What a host draws its terrain with when the renderer shades it: the game's own terrain fragment programs, linked with the host's
/// vertex stage, and the hooks inside a frame where it draws.
/// </summary>
public sealed partial class TerrainShading : IDisposable
{
    readonly GL _gl;
    readonly string _shadersDir;
    readonly ShaderBindings _bindings;
    MaterialBlock? _material;

    public const string TileLayerVarying = "wrs_tile_layer";

    public const int TileLayerLocation = 15;

    public const int NormalUnit = 17, MaterialUnit = 18, MaterialLinearUnit = 19, BakeUnit = 20, AlbedoArrayUnit = 13, CombinedArrayUnit = 14;
    public const uint TerrainSystemBinding = 11;

    internal TerrainShading(GL gl, string shadersDir, ShaderBindings bindings)
    {
        _gl = gl;
        _shadersDir = shadersDir;
        _bindings = bindings;
    }

    public bool Available => File.Exists(Path.Combine(_shadersDir, "terrain_prog2_extracted.frag"));

    public uint LinkGBufferProgram(string hostVertexSource, int program = 2)
    {
        string frag = File.ReadAllText(Path.Combine(_shadersDir, $"terrain_prog{program}_extracted.frag"));
        frag = PatchTileSamplers(TotkGlsl.Clean(frag));
        return GLProgramBuilder.Build(_gl, hostVertexSource, frag, $"terrain_prog{program}");
    }

    public uint LinkShadowProgram(string hostVertexSource) =>
        GLProgramBuilder.Build(_gl, hostVertexSource, GlslFiles.Load("Totk/Terrain/TerrainShading/ShadowStub.frag"), "terrain_shadow");

    static readonly Regex TileSamplerDeclaration = new(@"uniform\s+sampler2D\s+(cTeraTexNode\w+)\s*;", RegexOptions.Compiled);
    static readonly Regex TileSamplerRead = new(@"\b(texture|textureLod)\((cTeraTexNode\w+),\s*vec2\(", RegexOptions.Compiled);

    static string PatchTileSamplers(string source)
    {
        source = TileSamplerDeclaration.Replace(source, "uniform sampler2DArray $1;");
        // vec2(a, b) -> vec3(a, b, layer): the arguments are plain temporaries, never nested calls.
        source = TileSamplerRead.Replace(source, m => $"{m.Groups[1].Value}({m.Groups[2].Value}, vec3(");
        source = Regex.Replace(source, @"(\b(?:texture|textureLod)\(cTeraTexNode\w+, vec3\()([^()]*)\)",
            m => $"{m.Groups[1].Value}{m.Groups[2].Value}, {TileLayerVarying})");
        int at = source.IndexOf("layout (location", StringComparison.Ordinal);
        string declaration = $"layout (location = {TileLayerLocation}) flat in float {TileLayerVarying};\n";
        return at < 0 ? source : source.Insert(at, declaration);
    }

    // Mat[29].z, the terrain's wetness bias.
    const int DrySlotOffset = 29 * 16 + 8;

    internal MaterialBlock Material => _material ??= CreateMaterial();

    MaterialBlock CreateMaterial()
    {
        string path = Path.Combine(_shadersDir, "terrain_gsys_material.bin");
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        // The model's default has Mat slot 29 at 1, which the G-buffer program turns into full wet gloss. The game rewrites it with the
        // weather; dry ground is 0.
        if (bytes.Length >= DrySlotOffset + sizeof(float))
            BitConverter.TryWriteBytes(bytes.AsSpan(DrySlotOffset), 0f);
        return new MaterialBlock(_gl, bytes);
    }

    public void Dispose()
    {
        _material?.Dispose();
        DisposeWater();
    }
}
