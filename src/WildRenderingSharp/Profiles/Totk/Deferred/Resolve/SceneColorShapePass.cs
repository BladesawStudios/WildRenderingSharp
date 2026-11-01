using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// G-buffer shapes whose program reads the lit scene behind them - water above all - drawn the way the game draws them: after the
/// opaque scene is lit, over a copy of it.
/// </summary>
internal sealed class SceneColorShapePass : IDisposable
{
    readonly GL _gl;
    readonly uint _copyProgram, _white, _black;

    public const int MaterialIdUnit = 2, LinearDepthUnit = 4, LinearDepthHalfUnit = 5, PreFogUnit = 13, PreMiscUnit = 14, ColorBufferUnit = 27;

    static readonly string CopyFragmentSource = GlslFiles.Load("Totk/Deferred/SceneColorShape/Copy.frag");

    public SceneColorShapePass(GL gl)
    {
        _gl = gl;
        _copyProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, CopyFragmentSource, "scene_color_copy");
        _white = ConstantTextures.Rgba(gl, 1f, 1f, 1f, 1f);
        _black = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
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

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer)
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
        bool? fogBound = null;

        foreach (var group in groups)
        {
            var shapes = group.Shapes.Where(s => s.ReadsSceneColor);
            if (!shapes.Any())
                continue;
            group.BindUbos(resources);
            foreach (var sh in shapes)
            {
                if (fogBound != sh.FogsSceneColor)
                    BindFogInputs(targets, (fogBound = sh.FogsSceneColor).Value);
                FaceCulling.Apply(_gl, sh.RenderState);
                group.Draw(drawer, sh, ShapeProgram.GBuffer);
            }
        }
        FaceCulling.Reset(_gl);

        _gl.DepthFunc(DepthFunction.Less);
        _gl.Disable(EnableCap.DepthTest);
    }

    // A program that fogs what is behind it reads colour * PreMisc.w + PreFog at these units; the others read them as cTex_Projection0 and cTex_SkyIslandShadow,
    // where white is a projection that shades nothing and black no island's shadow. Left as pre-shading bound them, the fog read white and lit see-through eyes white.
    void BindFogInputs(RenderTargets targets, bool fogs)
    {
        BindAt(PreFogUnit, fogs ? targets.PreFog.Handle : _white);
        BindAt(PreMiscUnit, fogs ? targets.PreMisc.Handle : _black);
    }

    void BindAt(int unit, uint handle)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    public void Dispose()
    {
        _gl.ReleaseProgram(_copyProgram);
        _gl.DeleteTexture(_white);
        _gl.DeleteTexture(_black);
    }
}
