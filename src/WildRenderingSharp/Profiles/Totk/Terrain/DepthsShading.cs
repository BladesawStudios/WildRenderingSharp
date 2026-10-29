using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>
/// The game's own programs for the Depths' ground, the quad mesh (<c>cave_quad_mesh_generic</c>), fed by the host from the quad resource's page files.
/// </summary>
/// <remarks>
/// A quad mesh is drawn with no vertex buffer: the vertex stage reads <c>gl_VertexID</c> (a vertex's column and row in its quad, then the quad's index in its page file)
/// and takes everything else from the page file, bound as the storage buffer at <see cref="PageBinding"/>, and from three blocks, the draw call, the instance and the page
/// layout. It also indexes a table of immediate constants at run time, which the preparer writes beside the program. The fragment stage fetches each quad's normals and
/// material weights from two textures made out of the page file.
/// </remarks>
public sealed class DepthsShading
{
    // The blocks sit where nothing else of the renderer reads, not at the game's slots, which are the bones' and others'; linking points the program at them.
    // The page file is the storage buffer at 0, which the host puts back after it draws.
    public const uint ImmediatesBinding = 44, DrawCallBinding = 45, StaticBinding = 46, InstanceBinding = 47, PageLayoutBinding = 48, MaterialBinding = 8, PageBinding = 0;

    public const int NormalsUnit = 17, WeightsUnit = 18, DetailUnit = 19, AlbedoArrayUnit = TerrainShading.AlbedoArrayUnit, CombinedArrayUnit = TerrainShading.CombinedArrayUnit;

    // The block the vertex stage's table reads is declared by no stage of the program; the preparer's decompile leaves its name in the code and nothing else.
    const string PageBufferDeclaration = "layout (binding = 0, std430) buffer _vp_s0";

    const string ImmediatesDeclaration = "layout (binding = 2, std140) uniform _vp_c1\n{\n    precise vec4 data[4096];\n} vp_c1;\n\n";

    readonly GL _gl;
    readonly string _shadersDir;

    internal DepthsShading(GL gl, string shadersDir)
    {
        _gl = gl;
        _shadersDir = shadersDir;
    }

    public bool Available => new[] { "depths_prog1", "depths_prog2" }.All(p =>
        File.Exists(Path.Combine(_shadersDir, p + "_extracted.frag")) && File.Exists(Path.Combine(_shadersDir, p + "_extracted.vert"))
        && File.Exists(Path.Combine(_shadersDir, p + "_vp_c1.bin")))
        && File.Exists(Path.Combine(_shadersDir, "depths_gsys_material.bin"));

    public uint LinkGBufferProgram() => Link(2);

    public uint LinkDepthProgram() => Link(1);

    uint Link(int program)
    {
        var (vert, frag) = ExtractedProgramSource.Read(_shadersDir, $"depths_prog{program}");
        if (!vert.Contains(PageBufferDeclaration, StringComparison.Ordinal))
            throw new InvalidOperationException($"depths_prog{program}'s vertex stage declares no page buffer - the export changed?");
        vert = vert.Replace(PageBufferDeclaration, ImmediatesDeclaration + PageBufferDeclaration, StringComparison.Ordinal);
        uint linked = GLProgramBuilder.Build(_gl, vert, frag, $"depths_prog{program}");
        _gl.BindUniformBlock(linked, "_vp_c1", ImmediatesBinding);
        _gl.BindUniformBlock(linked, "_cave_QM_DrawCallDataUBO", DrawCallBinding);
        _gl.BindUniformBlock(linked, "_cave_StaticDataUBO", StaticBinding);
        _gl.BindUniformBlock(linked, "_cave_QM_InstanceDataUBO", InstanceBinding);
        _gl.BindUniformBlock(linked, "_cave_QM_PageLayoutDataUBO", PageLayoutBinding);
        return linked;
    }

    // The vertex stage's table of immediate constants, as the program was exported with it.
    public byte[] ImmediateConstants() => File.ReadAllBytes(Path.Combine(_shadersDir, "depths_prog2_vp_c1.bin"));

    // The authored bytes of the program's gsys_material block, which the host pads to the block's declared size.
    public byte[] MaterialDefaults() => File.ReadAllBytes(Path.Combine(_shadersDir, "depths_gsys_material.bin"));
}
