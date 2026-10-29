using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The game's gloom renderer: every volume's shape drawn into a density target, and that composed into the G-buffer as the game's miasma material.</summary>
/// <remarks>
/// The shape pass draws each volume's unit box over the screen against the G-buffer's depth, blended by MAX, then the cancelling ones with the program's cancelling
/// variant, blended by MIN. The Depths' altitude gloom is the same program over the whole screen. The compose pass reads that density, the depth and the
/// material IDs, and writes three outputs the game's pass writes: emission, the albedo whose alpha is a flag byte, and a material ID of 32 with the fade, which the
/// pass-ID stamp then names the <c>field_miasma</c> pass for. Run with the game's window origin on, over a G-buffer filled the same way.
/// </remarks>
internal sealed class GloomPass : IDisposable
{
    // The attachments the compose program's three output locations are written to: emission, albedo and material ID.
    const int EmissionAttachment = 5, AlbedoAttachment = RenderTargets.AlbedoAttachment, MaterialIdAttachment = 0;

    readonly GL _gl;
    readonly GloomPrograms _programs;
    readonly ScreenTarget _density;
    readonly GloomCube _cube;
    readonly HalfUnitQuad _quad;
    readonly uint _white;

    public GloomPass(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;
        _programs = new GloomPrograms(gl, programs);
        _density = new ScreenTarget(gl, InternalFormat.RG16f);
        _cube = new GloomCube(gl);
        _quad = new HalfUnitQuad(gl);
        _white = ConstantTextures.Rgba(gl, 1f, 1f, 1f, 1f);
    }

    public bool Available => _programs.Available;

    public void Run(GLResourceCache resources, RenderTargets targets, in CameraData camera, IGloomSource source, GloomTextures textures)
    {
        resources.Bind(GloomBlocks.BuildView(camera, targets.Width, targets.Height));
        resources.BindZeroed(GloomBlocks.Context);

        DrawDensity(resources, targets, camera, source, textures);
        Compose(resources, targets, source, textures);
    }

    void DrawDensity(GLResourceCache resources, RenderTargets targets, in CameraData camera, IGloomSource source, GloomTextures textures)
    {
        _density.Ensure(targets.Width, targets.Height);
        _density.BindCleared(0f);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        _gl.BindTextureAt(GloomPrograms.DepthUnit, targets.GBufferDepth.Handle);
        _gl.BindTextureAt(GloomPrograms.SimpleUnit, textures.GradientSimple, TextureTarget.Texture3D);
        _gl.BindTextureAt(GloomPrograms.FbmUnit, textures.GradientFbm, TextureTarget.Texture3D);
        _gl.BindTextureAt(GloomPrograms.RidgeUnit, textures.GradientRidge, TextureTarget.Texture3D);
        if (textures.Altitude is { } altitude && textures.Altitude2 is { } altitude2)
        {
            _gl.BindTextureAt(GloomPrograms.AltitudeUnit, altitude);
            _gl.BindTextureAt(GloomPrograms.Altitude2Unit, altitude2);
        }

        foreach (bool cancelling in (ReadOnlySpan<bool>)[false, true])
        {
            _gl.BlendEquation(cancelling ? BlendEquationModeEXT.Min : BlendEquationModeEXT.Max);
            foreach (var volume in source.Volumes.Where(v => v.Cancels == cancelling))
            {
                if (GloomBlocks.BuildArea(volume, camera, cancelling) is not { } area)
                    continue;
                resources.Bind(area);
                _gl.UseProgram(_programs.Volume(volume.Shape, cancelling));
                _cube.Draw();
            }

            if (!cancelling && source.AltitudeDensity is { } density && textures.Altitude is not null && textures.Altitude2 is not null)
            {
                resources.Bind(GloomBlocks.BuildAltitudeArea(density));
                _gl.UseProgram(_programs.Altitude());
                _quad.Draw();
            }
        }

        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.Disable(EnableCap.Blend);
    }

    void Compose(GLResourceCache resources, RenderTargets targets, IGloomSource source, GloomTextures textures)
    {
        // The compose program reads the material IDs it is also about to write.
        _gl.CopyImageSubData(targets.GBuffer[MaterialIdAttachment].Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            targets.MaterialIdCopy.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)targets.Width, (uint)targets.Height, 1);

        _gl.BindTextureAt(GloomPrograms.ComposeDepthUnit, targets.LinearDepth.Handle);
        _gl.BindTextureAt(GloomPrograms.DensityUnit, _density.Texture);
        _gl.BindTextureAt(GloomPrograms.VoronoiseUnit, textures.VoronoiseCloud, TextureTarget.Texture3D);
        _gl.BindTextureAt(GloomPrograms.ComposeSimpleUnit, textures.GradientSimple, TextureTarget.Texture3D);
        _gl.BindTextureAt(GloomPrograms.ComposeFbmUnit, textures.GradientFbm, TextureTarget.Texture3D);
        _gl.BindTextureAt(GloomPrograms.WorleyUnit, textures.WorleySimple, TextureTarget.Texture3D);
        // The game's darkness map, which the viewer leaves off: every place is lit.
        _gl.BindTextureAt(GloomPrograms.EnvironmentUnit, _white);
        _gl.BindTextureAt(GloomPrograms.MaterialIdUnit, targets.MaterialIdCopy.Handle);

        resources.Bind(GloomBlocks.BuildCompose(source.Look));
        targets.BindGBuffer();
        targets.SetGBufferOutputs(EmissionAttachment, AlbedoAttachment, MaterialIdAttachment);
        targets.SetGBufferColorMask(true);
        _gl.UseProgram(_programs.Compose());
        _quad.Draw();
        targets.SetGBufferOutputs();
        _gl.DepthMask(true);
    }

    public void Dispose()
    {
        _density.Dispose();
        _cube.Dispose();
        _quad.Dispose();
        _gl.DeleteTexture(_white);
    }
}
