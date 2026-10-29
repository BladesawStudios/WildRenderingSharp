using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The game's gloom renderer programs, <c>miasma_renderer_shape</c> and <c>miasma_renderer_compose</c>, linked as they are wanted and wired to <see cref="GloomBlocks"/>.</summary>
/// <remarks>
/// The shape program has a variant for each of its four shapes (sphere, box, cylinder, altitude) and for adding or cancelling, and the variant is the game's own:
/// the noise on, the half-resolution switch off (its code is the same either way), and the compose program's three correction switches off. A cancelling variant has no
/// context block, so what the adding variants read as their context, view and area, it reads as view and area one slot sooner. A sampler is named for its location
/// in the program, so what each location is depends on the variant - see the unit tables here.
/// </remarks>
internal sealed class GloomPrograms(GL gl, ShaderProgramCache programs)
{
    // The texture units the passes bind the gloom's inputs at.
    public const int DepthUnit = 0, SimpleUnit = 1, FbmUnit = 2, RidgeUnit = 3, AltitudeUnit = 4, Altitude2Unit = 5;

    public const int ComposeDepthUnit = 0, DensityUnit = 1, VoronoiseUnit = 2, ComposeSimpleUnit = 3, ComposeFbmUnit = 4, WorleyUnit = 5, EnvironmentUnit = 6, MaterialIdUnit = 7;

    const string ShapePrefix = "agl_miasma_renderer_shape_IS_ENABLE_NOISE1_IS_ENABLE_HALF0_BLEND_TYPE";

    const string ComposeName = "agl_miasma_renderer_compose_ENABLE_CORRECT0_ENABLE_TILE_MODE0_ENABLE_ALPHA_MODE0";

    const int AltitudeShapeType = 3;

    readonly Dictionary<(bool Cancelling, int Type), uint> _shapes = [];
    uint _compose;

    public bool Available => programs.Exists(ComposeName) && programs.Exists(ShapeName(false, 0)) && programs.Exists(ShapeName(true, 0));

    static string ShapeName(bool cancelling, int type) => $"{ShapePrefix}{(cancelling ? 1 : 0)}_SHAPE_TYPE{type}";

    public uint Volume(GloomShape shape, bool cancelling) => Shape(cancelling, (int)shape);

    public uint Altitude() => Shape(false, AltitudeShapeType);

    uint Shape(bool cancelling, int type)
    {
        if (_shapes.TryGetValue((cancelling, type), out uint linked))
            return linked;

        // The altitude program votes across a warp, and the cleaning leaves out the extension line that says so.
        uint program = type == AltitudeShapeType
            ? programs.Load(ShapeName(cancelling, type), patchFragment: src => src.Replace("#version 450 core", "#version 450 core\n#extension GL_ARB_shader_group_vote : enable"))
            : programs.Load(ShapeName(cancelling, type));
        gl.BindUniformBlock(program, "_vp_c3", GloomBlocks.View.Binding);
        gl.BindUniformBlock(program, "_vp_c4", GloomBlocks.Area.Binding);
        if (cancelling)
        {
            gl.BindUniformBlock(program, "_fp_c3", GloomBlocks.View.Binding);
            gl.BindUniformBlock(program, "_fp_c4", GloomBlocks.Area.Binding);
        }
        else
        {
            gl.BindUniformBlock(program, "_fp_c3", GloomBlocks.Context.Binding);
            gl.BindUniformBlock(program, "_fp_c4", GloomBlocks.View.Binding);
            gl.BindUniformBlock(program, "_fp_c5", GloomBlocks.Area.Binding);
        }

        gl.UseProgram(program);
        if (type == AltitudeShapeType)
        {
            // In this variant the altitude map is the sampler of location 0 and the depth the one of location 1.
            gl.SetSamplerUnit(program, "fp_t_tcb_C", DepthUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_8", AltitudeUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_A", Altitude2Unit);
            gl.SetSamplerUnit(program, "fp_t_tcb_E", SimpleUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_10", FbmUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_12", RidgeUnit);
        }
        else
        {
            gl.SetSamplerUnit(program, "fp_t_tcb_8", DepthUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_A", SimpleUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_C", FbmUnit);
            gl.SetSamplerUnit(program, "fp_t_tcb_E", RidgeUnit);
        }
        return _shapes[(cancelling, type)] = program;
    }

    public uint Compose()
    {
        if (_compose != 0)
            return _compose;

        uint program = programs.Load(ComposeName);
        gl.BindUniformBlock(program, "_vp_c3", GloomBlocks.View.Binding);
        gl.BindUniformBlock(program, "_fp_c3", GloomBlocks.View.Binding);
        gl.BindUniformBlock(program, "_fp_c4", GloomBlocks.Compose.Binding);
        gl.UseProgram(program);
        gl.SetSamplerUnit(program, "fp_t_tcb_8", ComposeDepthUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_A", DensityUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_C", VoronoiseUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_E", ComposeSimpleUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_10", ComposeFbmUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_12", WorleyUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_14", EnvironmentUnit);
        gl.SetSamplerUnit(program, "fp_t_tcb_16", MaterialIdUnit);
        return _compose = program;
    }
}
