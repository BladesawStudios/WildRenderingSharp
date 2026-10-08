using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// G-buffer shapes whose program reads the lit scene behind them - water above all - drawn the way the game draws them: after the
/// opaque scene is lit, over a copy of it.
/// </summary>
public sealed class SceneColorShapePass : IDisposable
{
    readonly GL _gl;
    readonly uint _copyProgram;

    public const int MaterialIdUnit = 2, LinearDepthUnit = 4, LinearDepthHalfUnit = 5, ColorBufferUnit = 27;

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
        _copyProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, CopyFragmentSource, "scene_color_copy");
    }

    public static bool Any(IReadOnlyList<ActorDrawGroup> groups) => groups.Any(g => g.Shapes.Any(s => s.ReadsSceneColor));

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
