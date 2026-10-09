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
    const uint ContextBinding = 25;
    const uint RenderInfoBinding = 26;

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
        bool vctx = _gl.BindUniformBlock(_program, "_vp_c4", ContextBinding);     // Context, vertex stage (location 1)
        bool fctx = _gl.BindUniformBlock(_program, "_fp_c3", ContextBinding);     // Context, pixel stage  (location 0)
        bool frin = _gl.BindUniformBlock(_program, "_fp_c4", RenderInfoBinding);  // RenderInfo, pixel stage
        _gl.UseProgram(_program);
        _gl.SetSamplerUnit(_program, "fp_t_tcb_8", 0);   // cTexBakedInscatter

        // The adhoc-fog variant declares the same blocks and sampler (it adds only pixel bytecode), so it needs the same rebinding.
        if (programs.Exists("agl_sky_postfx_sky_fog"))
        {
            _fogProgram = programs.Load("agl_sky_postfx_sky_fog");
            _gl.BindUniformBlock(_fogProgram, "_vp_c4", ContextBinding);
            _gl.BindUniformBlock(_fogProgram, "_fp_c3", ContextBinding);
            _gl.BindUniformBlock(_fogProgram, "_fp_c4", RenderInfoBinding);
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

    internal static byte[] BuildContext(ReadOnlySpan<Vector4> viewInv3Rows, float tanHalfFovX,
        float tanHalfFovY, float intensity, AdhocFog fog = default)
    {
        var buf = new byte[224];
        var u = new UniformWriter(buf);

        // View ray basis: v = (ndc.x * [0].x, ndc.y * [1].y, -1).
        u.Set(0, 0, tanHalfFovX);
        u.Set(1, 1, tanHalfFovY);
        u.Set(2, 3, -1f);

        // Camera-to-world rotation dotted row-wise against v, in the form BackgroundPass uses.
        void Row(int slot, Vector4 r) { u.Set(slot, 0, r.X); u.Set(slot, 1, r.Y); u.Set(slot, 2, r.Z); }
        Row(4, viewInv3Rows[0]);
        Row(5, viewInv3Rows[1]);
        Row(6, viewInv3Rows[2]);

        u.Set(13, 0, intensity);

        // Adhoc fog (slots 10 and 11), recovered by diffing which slots the USE_ADHOC_FOG=1 variant newly reads:
        //     master = sqrt(clamp([10].w * 4, 0, 1))                       (vertex -> in_attr1.x)
        //     up     = clamp(viewDir.y, 0, 1)                              (0 horizon, 1 zenith)
        //     scale  = mix([10].w, [10].z, pow(up, [10].y))
        //     rgb    = mix(skyColour, [11].xyz, scale * master)
        // A colour that saturates at the horizon and thins toward the zenith: the haze band. The three
        // scalars and the colour match adhoc_fog_atten_minscale_sky, adhoc_fog_atten_sky and adhoc_fog_color.
        u.Set(10, 1, fog.AttenSky);
        u.Set(10, 2, fog.ZenithScale);
        u.Set(10, 3, fog.Density);
        u.Set(11, 0, fog.Color.X); u.Set(11, 1, fog.Color.Y); u.Set(11, 2, fog.Color.Z);
        return buf;
    }

    /// <summary>The four values the <c>USE_ADHOC_FOG=1</c> sky program reads, resolved into the units it wants.</summary>
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

    internal static byte[] BuildRenderInfo(SkyPostFx postfx, Vector3 sunWorld, Vector3? fogColor = null,
        float paletteTint = 0f)
    {
        var buf = new byte[112];
        var u = new UniformWriter(buf);

        var br = postfx.RayleighScatteringCoeff;
        u.Set(0, 0, br.X); u.Set(0, 1, br.Y); u.Set(0, 2, br.Z); u.Set(0, 3, postfx.MieScatteringCoeff);

        Vector3 sun = sunWorld;
        if (sun.LengthSquared() > 1e-12f) sun = Vector3.Normalize(sun);
        u.Set(2, 0, sun.X); u.Set(2, 1, sun.Y); u.Set(2, 2, sun.Z);

        // The palette's FogColor when it has one, so the colour the table blends toward tracks the palette.
        var g = fogColor ?? postfx.GroundColor;
        u.Set(6, 0, g.X); u.Set(6, 1, g.Y); u.Set(6, 2, g.Z);
        // [6].w is the shader's blend bias: it computes clamp(lut.a + [6].w). 1 pins the weight so the table
        // wins, as in the capture (the game bakes per palette). One table is baked here, so lowering this lets
        // the palette's colour through by exactly that amount; 0 is the game's behaviour.
        u.Set(6, 3, 1f - Math.Clamp(paletteTint, 0f, 1f));
        return buf;
    }

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
        resources.Ubo("sky_support", SupportBufferUbo.Build(), SupportBufferUbo.BindingIndex);
        resources.Ubo("skyfx_context",
            BuildContext(viewInv3Rows, aspect * tanHalfFovY, tanHalfFovY, intensity, useFog ? fog : default), ContextBinding);
        resources.Ubo("skyfx_renderinfo", BuildRenderInfo(postfx, sunWorld, fogColor, paletteTint), RenderInfoBinding);

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
