using System.Numerics;
using System.Text.RegularExpressions;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// What a host draws its terrain with when the renderer shades it: the game's own terrain fragment
/// programs, linked with the host's vertex stage, and the hooks inside a frame where it draws.
/// </summary>
/// <remarks>
/// <para>
/// The game's terrain (<c>Shader/terrain</c>, exported by the preparer - see
/// <c>ShaderLibrary.CompileTool.ExportTerrainShaders</c>) is heightmap patches. Its vertex stage is
/// specific to the game's patch scheme, which a host has its own version of - levels, masks,
/// skirts - so the host keeps its geometry and supplies a vertex stage that writes the eight
/// varyings the game's vertex stage would (locations 0-7, in the game's Y-up world), plus
/// <see cref="TileLayerVarying"/>. Everything the fragment stage does - the material table, the
/// two-material blend, the detail normals, the bake, the G-buffer encoding - is the game's.
/// </para>
/// <para>
/// The game samples one 2D texture per patch; a host keeps tiles as layers of arrays. So the
/// program's <c>cTeraTexNode*</c> samplers become arrays, read at <see cref="TileLayerVarying"/>.
/// </para>
/// <para>
/// Units: the host binds <c>cTeraTexNodeNormal</c> (17), <c>cTeraTexNodeMaterial</c> (18, nearest),
/// <c>cTeraTexNodeMaterialLinear</c> (19, the same data filtered), <c>cTeraTexNodeBake</c> (20), and
/// the material arrays <c>MaterialAlb</c> (13, sRGB) and <c>MaterialCmb</c> (14). The renderer
/// binds the rest: <c>Context</c> (1) in the game's Y-up world, <c>gsys_material</c> (8) with the
/// terrain model's defaults, and the G-buffer albedo (0), normal (1) and linear depth (4) under the
/// terrain - drawn after the actors, as the game does, for the soft edge where ground meets an
/// object set into it. The host fills <c>TerrainSystem</c> (11) itself.
/// </para>
/// </remarks>
public sealed partial class TerrainShading : IDisposable
{
    readonly GL _gl;
    readonly string _shadersDir;
    uint _materialBuffer;

    /// <summary>The flat varying a host's vertex stage writes the tile's array layer to.</summary>
    public const string TileLayerVarying = "wrs_tile_layer";

    /// <summary>Its location.</summary>
    public const int TileLayerLocation = 15;

    public const int NormalUnit = 17, MaterialUnit = 18, MaterialLinearUnit = 19, BakeUnit = 20, AlbedoArrayUnit = 13, CombinedArrayUnit = 14;
    public const uint TerrainSystemBinding = 11;

    internal TerrainShading(GL gl, string shadersDir)
    {
        _gl = gl;
        _shadersDir = shadersDir;
    }

    /// <summary>Whether the preparer has exported the terrain programs into the cache.</summary>
    public bool Available => File.Exists(Path.Combine(_shadersDir, "terrain_prog2_extracted.frag"));

    /// <summary>
    /// Links the game's terrain G-buffer program <paramref name="program"/> (2, or the coarser 32
    /// and 62) with <paramref name="hostVertexSource"/>.
    /// </summary>
    public uint LinkGBufferProgram(string hostVertexSource, int program = 2)
    {
        string frag = File.ReadAllText(Path.Combine(_shadersDir, $"terrain_prog{program}_extracted.frag"));
        frag = PatchTileSamplers(GlslSanitizer.Clean(frag));
        return GLProgramBuilder.Build(_gl, hostVertexSource, frag, $"terrain_prog{program}");
    }

    /// <summary>Links <paramref name="hostVertexSource"/> with an empty fragment stage, for drawing the terrain into a shadow map.</summary>
    public uint LinkShadowProgram(string hostVertexSource) =>
        GLProgramBuilder.Build(_gl, hostVertexSource, "#version 450 core\nvoid main() { }\n", "terrain_shadow");

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

    /// <summary>The terrain model's default <c>gsys_material</c>, bound at 8 while the terrain draws.</summary>
    internal uint MaterialBuffer
    {
        get
        {
            if (_materialBuffer == 0)
            {
                string path = Path.Combine(_shadersDir, "terrain_gsys_material.bin");
                byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
                _materialBuffer = Assets.GLBuffer.CreatePaddedUniformBuffer(_gl, bytes);
            }
            return _materialBuffer;
        }
    }

    /// <summary>
    /// Rows written for the renderer's Z-up world (<c>row · (p, 1)</c>) re-expressed for a point in
    /// the game's Y-up world, which the renderer's world is a quarter turn from: <c>(x, y, z)</c>
    /// is <c>(x, -z, y)</c> there.
    /// </summary>
    internal static Vector4[] FromYUp(ReadOnlySpan<Vector4> rows)
    {
        var result = new Vector4[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            result[i] = new Vector4(rows[i].X, rows[i].Z, -rows[i].Y, rows[i].W);
        return result;
    }

    /// <summary>A view-to-world inverse for the Z-up world, as one for the Y-up world.</summary>
    internal static Vector4[] InverseToYUp(ReadOnlySpan<Vector4> viewInv3) => [viewInv3[0], viewInv3[2], -viewInv3[1]];

    public void Dispose()
    {
        if (_materialBuffer != 0)
            _gl.DeleteBuffer(_materialBuffer);
        DisposeWater();
    }
}

/// <summary>A host whose terrain the renderer shades - see <see cref="TerrainShading"/>.</summary>
public interface ITerrainHost
{
    /// <summary>Changes whenever what <see cref="DrawShadow"/> draws would change - tiles loaded, edited.</summary>
    long ShadowVersion { get; }

    /// <summary>Draws the terrain into the G-buffer, which is bound with the state set (see <see cref="TerrainShading"/>'s units).</summary>
    void DrawGBuffer(TerrainDraw draw);

    /// <summary>Draws the terrain's depth into a shadow cascade, which is bound, with the light's <c>Context</c> at 1.</summary>
    void DrawShadow(TerrainDraw draw);

    /// <summary>Whether the host draws its water through the game's water program this frame (see <see cref="TerrainShading.LinkWaterProgram"/>).</summary>
    bool HasWater => false;

    /// <summary>
    /// Draws the water with the program from <see cref="TerrainShading.LinkWaterProgram"/> - or, when
    /// <paramref name="stamp"/>, the same geometry with the one from
    /// <see cref="TerrainShading.LinkWaterStampProgram"/>, which marks its pixels for the water's
    /// deferred pass. Everything but the host's own textures and <c>TerrainSystem</c> is bound.
    /// </summary>
    void DrawWater(TerrainDraw draw, bool stamp) { }
}

/// <param name="CameraYUp">The camera's position in the game's Y-up world.</param>
/// <param name="Cascade">The shadow cascade being drawn, or -1 for the G-buffer.</param>
/// <param name="Region">The region the cascade covers (Y-up centre, radius) - for culling tiles outside it.</param>
public readonly record struct TerrainDraw(GL Gl, Vector3 CameraYUp, int Cascade, Vector4 Region);
