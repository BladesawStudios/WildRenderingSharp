using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>
/// The game's own programs for the Depths' ground, the quad mesh (<c>cave_quad_mesh_generic</c>), fed by the host from the quad resource's page files.
/// </summary>
/// <remarks>
/// A quad mesh is drawn with no vertex buffer. The vertex stage reads only <c>gl_VertexID</c>, whose low five bits are a vertex's column in its quad, the next five its
/// row, and the rest the quad's index in its page file, and takes the heights, the quad's position and its neighbours from the page file itself, which is bound as the
/// storage buffer at <see cref="PageBinding"/>. Three blocks say how to read it: the draw call (<see cref="DrawCallBinding"/>: the level of detail, the node's coordinates
/// and the size of a unit), the instance (<see cref="InstanceBinding"/>: the placement and the tree's depths) and the page layout (<see cref="PageLayoutBinding"/>: where
/// the page file keeps each array). It also indexes a table of immediate constants at run time, which the preparer writes beside the program
/// (<see cref="ImmediateConstants"/>) and the host binds at <see cref="ImmediatesBinding"/>. The fragment stage fetches the normals and the material weights of each
/// quad from two textures made out of the page file (<see cref="NormalsUnit"/>, <see cref="WeightsUnit"/>) and reads each material layer's tiling from
/// <see cref="StaticBinding"/>.
/// </remarks>
public sealed class DepthsShading
{
    // The blocks are bound where nothing else of the renderer reads (the game has them at 2, 3, 11, 12 and 13, which are the bones' and others'); the program is
    // pointed at them when it is linked. The page file is the storage buffer at 0, which the host puts back after it draws.
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
        string vert = TotkGlsl.Clean(File.ReadAllText(Path.Combine(_shadersDir, $"depths_prog{program}_extracted.vert")));
        if (!vert.Contains(PageBufferDeclaration, StringComparison.Ordinal))
            throw new InvalidOperationException($"depths_prog{program}'s vertex stage declares no page buffer - the export changed?");
        vert = vert.Replace(PageBufferDeclaration, ImmediatesDeclaration + PageBufferDeclaration, StringComparison.Ordinal);
        string frag = UnwrittenOutputs.Apply(TotkGlsl.Clean(File.ReadAllText(Path.Combine(_shadersDir, $"depths_prog{program}_extracted.frag"))));
        uint linked = GLProgramBuilder.Build(_gl, vert, frag, $"depths_prog{program}");
        _gl.BindUniformBlock(linked, "_vp_c1", ImmediatesBinding);
        _gl.BindUniformBlock(linked, "_cave_QM_DrawCallDataUBO", DrawCallBinding);
        _gl.BindUniformBlock(linked, "_cave_StaticDataUBO", StaticBinding);
        _gl.BindUniformBlock(linked, "_cave_QM_InstanceDataUBO", InstanceBinding);
        _gl.BindUniformBlock(linked, "_cave_QM_PageLayoutDataUBO", PageLayoutBinding);
        return linked;
    }

    /// <summary>The vertex stage's table of immediate constants, as the program was exported with it.</summary>
    public byte[] ImmediateConstants() => File.ReadAllBytes(Path.Combine(_shadersDir, "depths_prog2_vp_c1.bin"));

    /// <summary>The authored bytes of the program's <c>gsys_material</c> block, which the host pads to the block's declared size.</summary>
    public byte[] MaterialDefaults() => File.ReadAllBytes(Path.Combine(_shadersDir, "depths_gsys_material.bin"));
}
