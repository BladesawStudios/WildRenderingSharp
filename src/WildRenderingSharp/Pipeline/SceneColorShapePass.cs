using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// G-buffer shapes whose program reads the lit scene behind them - water above all - drawn the
/// way the game draws them: after the opaque scene is lit, over a copy of it.
/// </summary>
/// <remarks>
/// <para>
/// Water's G-buffer program (<c>Mt_TerraWater_A</c>, <c>o_material_behave</c> 103) samples
/// <c>cTex_ColorBuffer</c> at a normal-offset screen position, attenuates it by how much water lies
/// between the surface and the opaque depth under it (<c>cTex_NormalizedLinearDepth</c>, with which
/// it also discards itself where it is behind the scene), and writes the result to the emission
/// attachment with the emission bit set in albedo alpha - refraction, delivered through the same
/// emission compose every other surface uses. Its albedo carries only the water's own scatter
/// colour, which <c>field_water</c> lights. It also reads <c>cTex_GBuffMaterialID</c> (attachment
/// 0) to fold the material under it into its own ID. Drawn with the rest of the G-buffer, every one
/// of those inputs is either empty or the very target being written, and the water came out black.
/// </para>
/// <para>
/// So the frame runs in two halves. The opaque half is lit and resolved as usual, without these
/// shapes. Then the lit scene is copied (into the G-buffer's orientation, and into the game's
/// units - see <see cref="CopyInputs"/>), so is attachment 0, and these shapes draw into the same
/// G-buffer over the opaque depth. The caller then re-derives the screen-space inputs and resolves
/// only these shapes' passes, which composite only where the pass-ID mask names them.
/// </para>
/// </remarks>
public sealed class SceneColorShapePass : IDisposable
{
    readonly GL _gl;
    readonly uint _copyProgram;

    /// <summary>The units the game's programs read these from - fixed by their own bindings.</summary>
    public const int MaterialIdUnit = 2, LinearDepthUnit = 4, LinearDepthHalfUnit = 5, ColorBufferUnit = 27;

    const string QuadVertexSource = """
        #version 450 core
        out vec2 vUV;
        void main() {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    const string CopyFragmentSource = """
        #version 450 core
        uniform sampler2D t;
        uniform float uScale;
        in vec2 vUV;
        out vec4 fragColor;
        void main() {
            vec3 c = texture(t, vec2(vUV.x, 1.0 - vUV.y)).rgb;
            if (any(isnan(c)) || any(isinf(c))) c = vec3(0.0);
            fragColor = vec4(max(c, vec3(0.0)) * uScale, 1.0);
        }
        """;

    public SceneColorShapePass(GL gl)
    {
        _gl = gl;
        _copyProgram = GLProgramBuilder.Build(gl, QuadVertexSource, CopyFragmentSource, "scene_color_copy");
    }

    /// <summary>Whether any group has a shape this pass draws.</summary>
    public static bool Any(IReadOnlyList<ActorDrawGroup> groups) => groups.Any(g => g.Shapes.Any(s => s.ReadsSceneColor));

    /// <summary>
    /// Copies the lit opaque scene into <see cref="RenderTargets.Behind"/> as <c>cTex_ColorBuffer</c>,
    /// and attachment 0 into <see cref="RenderTargets.MaterialIdCopy"/> as <c>cTex_GBuffMaterialID</c>.
    /// </summary>
    /// <param name="emissionUnits">
    /// What turns <see cref="RenderTargets.Final"/>'s values into the units emission is authored
    /// in. The compose step divides emission by the exposure and multiplies by the Emission Scale
    /// (see <see cref="DeferredResolvePass.Run"/>), so a refracted pixel lands at the brightness of
    /// the scene it shows only if the copy carries the inverse of that.
    /// </param>
    public void CopyInputs(GLResourceCache resources, RenderTargets targets, float emissionUnits)
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        targets.BindColorTarget(targets.Behind);
        _gl.UseProgram(_copyProgram);
        _gl.BindTextureUniform(_copyProgram, "t", 0, targets.Final.Handle);
        _gl.SetFloat(_copyProgram, "uScale", emissionUnits);
        resources.DrawFullscreenTriangle();

        _gl.CopyImageSubData(targets.GBuffer[0].Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            targets.MaterialIdCopy.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            (uint)targets.Width, (uint)targets.Height, 1);
    }

    /// <summary>
    /// Draws these shapes into the G-buffer, over the opaque depth. The flipped-projection
    /// <c>Context</c> must be bound at binding 1, as for <see cref="GBufferPass"/>.
    /// </summary>
    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShaderProgramCache programs)
    {
        targets.BindGBuffer();
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);

        BindAt(MaterialIdUnit, targets.MaterialIdCopy.Handle);
        BindAt(LinearDepthUnit, targets.LinearDepth.Handle);
        BindAt(LinearDepthHalfUnit, targets.LinearDepthHalf.Handle);
        BindAt(ColorBufferUnit, targets.Behind.Handle);

        foreach (var group in groups)
        {
            var shapes = group.Shapes.Where(s => s.ReadsSceneColor);
            if (!shapes.Any())
                continue;
            group.BindUbos(resources);
            foreach (var sh in shapes)
                group.Draw(_gl, programs, sh, ShapeProgram.GBuffer);
        }

        _gl.DepthFunc(DepthFunction.Less);
        _gl.Disable(EnableCap.DepthTest);
    }

    void BindAt(int unit, uint handle)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    public void Dispose() => _gl.DeleteProgram(_copyProgram);
}
