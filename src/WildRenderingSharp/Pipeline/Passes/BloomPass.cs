using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>A bright pass, four downsampled and blurred levels, and a weighted compose that feeds the game's own <c>agl_hdr_compose</c> through its <c>cBloom</c> sampler.</summary>
public sealed class BloomPass : IDisposable
{
    public static readonly Vector4[] LevelColors =
    [
        new(1f, 1f, 1f, 0.50f),
        new(1f, 1f, 1f, 1.00f),
        new(1f, 0.48f, 0.30f, 1.00f),
        new(1f, 0.48f, 0.30f, 0.00f),
    ];
    public static readonly Vector3 ComposeColor = new(1.0f, 0.935f, 0.833f);
    public static readonly Vector3 Balance = Vector3.One;

    static readonly string BrightFragmentSource = GlslFiles.Load("Pipeline/Bloom/Bright.frag");
    static readonly string BlurFragmentSource = GlslFiles.Load("Pipeline/Bloom/Blur.frag");
    static readonly string ComposeFragmentSource = GlslFiles.Load("Pipeline/Bloom/Compose.frag");

    readonly GL _gl;
    readonly uint _brightProgram, _blurProgram, _composeProgram;

    public BloomPass(GL gl)
    {
        _gl = gl;
        _brightProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, BrightFragmentSource, "bloom_bright");
        _blurProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, BlurFragmentSource, "bloom_blur");
        _composeProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ComposeFragmentSource, "bloom_compose");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture hdrSource, float threshold, float clamp, float intensity)
    {
        _gl.Disable(EnableCap.DepthTest);

        var source = hdrSource;
        for (int i = 0; i < RenderTargets.BloomLevelCount; i++)
        {
            var level = targets.BloomLevels[i];
            targets.BindColorTarget(level);
            if (i == 0)
                DrawBright(resources, source, threshold, clamp);
            else
                DrawBlur(resources, source, new Vector2(1f / level.Width, 0f));
            BlurLevel(resources, targets, level, targets.BloomTmp[i]);
            source = level;
        }

        Compose(resources, targets, intensity);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_brightProgram);
        _gl.DeleteProgram(_blurProgram);
        _gl.DeleteProgram(_composeProgram);
    }

    void DrawBright(GLResourceCache resources, GpuTexture source, float threshold, float clamp)
    {
        _gl.UseProgram(_brightProgram);
        _gl.SetVec3(_brightProgram, "uBalance", Balance);
        _gl.SetFloat(_brightProgram, "uThreshold", threshold);
        _gl.SetFloat(_brightProgram, "uClamp", clamp);
        _gl.BindTextureUniform(_brightProgram, "t", 0, source.Handle);
        resources.DrawFullscreenTriangle();
    }

    void DrawBlur(GLResourceCache resources, GpuTexture source, Vector2 step)
    {
        _gl.UseProgram(_blurProgram);
        _gl.BindTextureUniform(_blurProgram, "t", 0, source.Handle);
        _gl.SetVec2(_blurProgram, "uStep", step);
        resources.DrawFullscreenTriangle();
    }

    // Horizontal into the scratch target, then vertical back into the level.
    void BlurLevel(GLResourceCache resources, RenderTargets targets, GpuTexture level, GpuTexture scratch)
    {
        targets.BindColorTarget(scratch);
        DrawBlur(resources, level, new Vector2(1f / level.Width, 0f));
        targets.BindColorTarget(level);
        DrawBlur(resources, scratch, new Vector2(0f, 1f / level.Height));
    }

    void Compose(GLResourceCache resources, RenderTargets targets, float intensity)
    {
        targets.BindColorTarget(targets.Bloom);
        _gl.UseProgram(_composeProgram);
        for (int i = 0; i < RenderTargets.BloomLevelCount; i++)
        {
            _gl.BindTextureUniform(_composeProgram, $"l{i}", i, targets.BloomLevels[i].Handle);
            _gl.SetVec4(_composeProgram, $"c{i}", LevelColors[i]);
        }
        _gl.SetVec3(_composeProgram, "uCompose", ComposeColor);
        _gl.SetFloat(_composeProgram, "uIntensity", intensity);
        resources.DrawFullscreenTriangle();
    }
}
