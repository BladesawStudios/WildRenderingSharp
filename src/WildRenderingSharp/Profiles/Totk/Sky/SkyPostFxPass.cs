using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Shaders;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Draws the sky with the game's <c>agl_sky_postfx_sky</c> program, sampling the baked inscatter table <see
/// cref="SkyPrecomputePass"/> produces.
/// </summary>
public sealed class SkyPostFxPass : IDisposable
{
    // The decompiled per-stage indices collide once both stages share a program, and they disagree on
    // meaning: Context is location 0 in the pixel stage but 1 in the vertex stage, so vp_c4 and fp_c3 are
    // the same block while fp_c4 is a different one. Rebinding explicitly is the only way that coexists.

    readonly GL _gl;
    readonly uint _program;
    readonly uint _fogProgram;
    readonly uint _vao, _vbo;

    public bool Available => _program != 0;
    public bool FogAvailable => _fogProgram != 0;
    bool _logged;

    public unsafe SkyPostFxPass(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;
        if (!programs.Exists("agl_sky_postfx_sky"))
        {
            Console.WriteLine("[SkyPostFxPass] agl_sky_postfx_sky not in the shader cache - disabled.");
            return;
        }

        _program = programs.Load("agl_sky_postfx_sky");
        bool vctx = _gl.BindUniformBlock(_program, "_vp_c4", SkyPostFxBlocks.Context.Binding);     // Context, vertex stage (location 1)
        bool fctx = _gl.BindUniformBlock(_program, "_fp_c3", SkyPostFxBlocks.Context.Binding);     // Context, pixel stage  (location 0)
        bool frin = _gl.BindUniformBlock(_program, "_fp_c4", SkyPostFxBlocks.RenderInfo.Binding);  // RenderInfo, pixel stage
        _gl.UseProgram(_program);
        _gl.SetSamplerUnit(_program, "fp_t_tcb_8", 0);   // cTexBakedInscatter

        // The adhoc-fog variant declares the same blocks and sampler (it adds only pixel bytecode), so it needs the same rebinding.
        if (programs.Exists("agl_sky_postfx_sky_fog"))
        {
            _fogProgram = programs.Load("agl_sky_postfx_sky_fog");
            _gl.BindUniformBlock(_fogProgram, "_vp_c4", SkyPostFxBlocks.Context.Binding);
            _gl.BindUniformBlock(_fogProgram, "_fp_c3", SkyPostFxBlocks.Context.Binding);
            _gl.BindUniformBlock(_fogProgram, "_fp_c4", SkyPostFxBlocks.RenderInfo.Binding);
            _gl.UseProgram(_fogProgram);
            _gl.SetSamplerUnit(_fogProgram, "fp_t_tcb_8", 0);
        }

        Console.WriteLine($"[SkyPostFxPass] real agl_sky_postfx_sky linked - blocks rebound: " +
            $"Context(vert)={vctx}, Context(frag)={fctx}, RenderInfo(frag)={frin}; " +
            $"adhoc-fog variant={(_fogProgram != 0 ? "yes" : "MISSING (re-run sky shader extraction)")}");

        // Half-unit quad: the vertex stage does gl_Position.xy = in_attr0.xy * 2.0.
        Vector4[] quad =
        [
            new Vector4(-0.5f, -0.5f, 0f, 1f),
            new Vector4( 0.5f, -0.5f, 0f, 1f),
            new Vector4(-0.5f,  0.5f, 0f, 1f),
            new Vector4( 0.5f,  0.5f, 0f, 1f),
        ];
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vector4* p = quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(Vector4)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vector4), (void*)0);
        gl.BindVertexArray(0);
    }

    // The four values the USE_ADHOC_FOG=1 sky program reads, resolved into the units it wants.
    public readonly record struct AdhocFog(float AttenSky, float ZenithScale, float Density, Vector3 Color);

    public static AdhocFog Resolve(EnvPalette palette, SkyPostFx postfx, float skyIntensity, float strength,
        bool normaliseHue = true)
    {
        // The palette's authored alpha where it has one, the captured noon value as the floor, so a palette that authors nothing keeps the haze the game has.
        float density = MathF.Max(Math.Clamp(palette.FogDensity, 0f, 1f), CapturedNoonDensity)
            * MathF.Max(0f, strength);

        // The captured value, not the file's 0.764 (see the remarks). Guarded strictly positive: it is a pow() exponent (see AttenSky).
        float atten = CapturedAttenSky > 1e-4f ? CapturedAttenSky : 0.5f;

        // Raw, per the capture, but in this renderer's sky units: the game runs this against the same raw table with
        // Context[13].x = 1, and skyIntensity stands in for that one.
        var colour = palette.FogColor;
        if (normaliseHue)
        {
            float peak = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));
            if (peak > 1e-6f) colour /= peak;
        }
        return new AdhocFog(atten, Math.Clamp(postfx.AdhocFogAttenMinScaleSky, 0f, 1f), density,
            colour * skyIntensity);
    }

    public const float CapturedNoonDensity = 0.089538f;

    public const float CapturedAttenSky = 0.5f;

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture target,
        uint bakedInscatter, ReadOnlySpan<Vector4> viewInv3Rows, float aspect, float tanHalfFovY,
        Vector3 sunWorld, SkyPostFx postfx, float intensity, Vector3? fogColor = null,
        float paletteTint = 0f, AdhocFog fog = default)
    {
        if (!Available || bakedInscatter == 0)
            return;

        // The fog program is used only when there is fog: at density 0 the two give identical output, and this keeps a missing fog variant harmless.
        bool useFog = fog.Density > 1e-5f && _fogProgram != 0;
        uint program = useFog ? _fogProgram : _program;

        if (!_logged)
        {
            _logged = true;
            Console.WriteLine($"[SkyPostFxPass] first draw: lut={bakedInscatter} intensity={intensity:G6} " +
                $"aspect={aspect:G6} tanHalfFovY={tanHalfFovY:G6} sun={sunWorld} " +
                $"adhocFog={(useFog ? $"density {fog.Density:G4}, atten {fog.AttenSky:G4}, colour {fog.Color}" : "off")}");
        }
        resources.Bind(SupportBuffer.Block);
        resources.Bind(SkyPostFxBlocks.BuildContext(viewInv3Rows, aspect * tanHalfFovY, tanHalfFovY, intensity, useFog ? fog : default));
        resources.Bind(SkyPostFxBlocks.BuildRenderInfo(postfx, sunWorld, fogColor, paletteTint));

        targets.BindColorTarget(target);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, bakedInscatter);

        _gl.UseProgram(program);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
    }
}
