using System.Numerics;
using WildRenderingSharp.Rendering;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Draws the sky with the game's OWN <c>agl_sky_postfx_sky</c> program, sampling the baked
/// inscatter LUT that <see cref="SkyPrecomputePass"/> produces. This is the real thing rather than
/// <c>BackgroundPass</c>'s hand-written Rayleigh+Mie raymarch.
/// </summary>
/// <remarks>
/// <para>
/// The per-frame sky really is this cheap - one full-screen quad, one 2D texture lookup, one lerp.
/// All the physics lives in the LUT. The whole fragment program is:
/// </para>
/// <code>
/// vec3  d   = normalize(in_attr0.xyz);                      // view ray, Y-UP
/// float t   = dot(d, RenderInfo[2].xyz) * 0.5 + 0.5;        // sun-view angle
/// vec2  uv  = vec2(1.0 - acos(t) * (2.0/PI), d.y * 0.5 + 0.5);
/// vec4  lut = texture(cTexBakedInscatter, uv);
/// out.rgb   = mix(RenderInfo[6].xyz, lut.rgb * Context[13].x,
///                 clamp(lut.a + RenderInfo[6].w, 0.0, 1.0));
/// </code>
/// <para>
/// <b>The vertex stage is what needs Context</b>, and its shape was read off the ISA and then
/// confirmed against a real capture. It builds a view ray
/// <c>v = (ndc.x * Context[0].x, ndc.y * Context[1].y, -1)</c> and rotates it by rows
/// <c>[4]/[5]/[6]</c>, dotting each row against <c>v</c>. In the capture those first two were
/// <c>0.82899</c> and <c>0.46631</c> - a 16:9 aspect and a 50 degree vertical FOV - which is what
/// identifies them as <c>tanHalfFovX</c>/<c>tanHalfFovY</c>, and <c>[5]</c> came back as
/// <c>(0, 0.9987, 0.05)</c>, i.e. world up landing in <c>.y</c>. That is the tell that the ray is
/// produced in <b>Y-up</b> space, which the fragment's <c>d.y</c> elevation lookup requires.
/// </para>
/// <para>
/// WildRenderingSharp's world is Z-up, so rows 1 and 2 are swapped when filling <c>[4..6]</c> - the same
/// Y-up/Z-up boundary <see cref="CloudDomePass"/> has to cross, and just as easy to get backwards
/// (it shows up as a sky whose gradient runs sideways rather than as an obvious error).
/// </para>
/// </remarks>
public sealed class SkyPostFxPass : IDisposable
{
    // Chosen bindings: the decompiled per-stage NVN indices collide once both stages are linked
    // into one program, and here they genuinely disagree about meaning - Context is at location 0
    // for the pixel stage but location 1 for the vertex stage, so vp_c4 and fp_c3 are the SAME
    // block while fp_c4 is a different one. Rebinding explicitly is the only way that coexists.
    const uint ContextBinding = 25;
    const uint RenderInfoBinding = 26;

    readonly GL _gl;
    readonly uint _program;
    /// <summary>The <c>USE_ADHOC_FOG=1</c> variant - identical inputs, so it is a drop-in swap. 0 if it is not in the cache (an older cache predating its extraction).</summary>
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
        bool vctx = BindBlock("_vp_c4", ContextBinding);     // Context, vertex stage (location 1)
        bool fctx = BindBlock("_fp_c3", ContextBinding);     // Context, pixel stage  (location 0)
        bool frin = BindBlock("_fp_c4", RenderInfoBinding);  // RenderInfo, pixel stage
        _gl.UseProgram(_program);
        int loc = _gl.GetUniformLocation(_program, "fp_t_tcb_8");   // cTexBakedInscatter
        if (loc >= 0) _gl.Uniform1(loc, 0);

        // The adhoc-fog variant declares the SAME blocks and the SAME single sampler (confirmed
        // from the archive's own macro table: USE_ADHOC_FOG adds no sampler and no block, only
        // pixel bytecode), so it needs the identical rebinding and can be swapped in per frame.
        if (programs.Exists("agl_sky_postfx_sky_fog"))
        {
            _fogProgram = programs.Load("agl_sky_postfx_sky_fog");
            BindBlockOn(_fogProgram, "_vp_c4", ContextBinding);
            BindBlockOn(_fogProgram, "_fp_c3", ContextBinding);
            BindBlockOn(_fogProgram, "_fp_c4", RenderInfoBinding);
            _gl.UseProgram(_fogProgram);
            int fogLoc = _gl.GetUniformLocation(_fogProgram, "fp_t_tcb_8");
            if (fogLoc >= 0) _gl.Uniform1(fogLoc, 0);
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

    bool BindBlock(string name, uint binding) => BindBlockOn(_program, name, binding);

    bool BindBlockOn(uint program, string name, uint binding)
    {
        uint idx = _gl.GetUniformBlockIndex(program, name);
        if (idx == 0xFFFFFFFFu)
            return false;
        _gl.UniformBlockBinding(program, idx, binding);
        return true;
    }

    /// <summary>WildRenderingSharp's Z-up world vector as the Y-up one every agl sky/cloud shader expects.</summary>
    static Vector3 ToYUp(Vector3 v) => new(v.X, v.Z, v.Y);

    /// <summary>The 224-byte <c>Context</c> block (14 vec4). Only the slots this program reads are filled.</summary>
    internal static byte[] BuildContext(ReadOnlySpan<Vector4> viewInv3Rows, float tanHalfFovX,
        float tanHalfFovY, float intensity, AdhocFog fog = default)
    {
        var buf = new byte[224];
        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).CopyTo(buf, slot * 16 + comp * 4);

        // View ray basis: v = (ndc.x * [0].x, ndc.y * [1].y, -1).
        F(0, 0, tanHalfFovX);
        F(1, 1, tanHalfFovY);
        F(2, 3, -1f);

        // Camera-to-world rotation, dotted row-wise against v. WildRenderingSharp's own inverse-view rows are
        // already in exactly this form (BackgroundPass does mat3(uViewInv) * viewDir with the same
        // ray convention) - the only change is Z-up to Y-up, which swaps which row feeds .y and .z.
        void Row(int slot, Vector4 r) { F(slot, 0, r.X); F(slot, 1, r.Y); F(slot, 2, r.Z); }
        Row(4, viewInv3Rows[0]);   // -> x
        Row(5, viewInv3Rows[2]);   // Z-up z becomes Y-up y
        Row(6, viewInv3Rows[1]);   // Z-up y becomes Y-up z

        F(13, 0, intensity);

        // ---- adhoc fog (slots 10 and 11) ----
        // Recovered by decompiling sky_postfx_sky at USE_ADHOC_FOG=0 and =1 and diffing which
        // slots the =1 variant newly reads: exactly [10].y/.z/.w and [11].xyz in the pixel stage,
        // plus [10].w again in the vertex stage. The resulting program is:
        //
        //     master = sqrt(clamp([10].w * 4, 0, 1))                       (vertex -> in_attr1.x)
        //     up     = clamp(viewDir.y, 0, 1)                              (0 horizon, 1 zenith)
        //     scale  = mix([10].w, [10].z, pow(up, [10].y))
        //     rgb    = mix(skyColour, [11].xyz, scale * master)
        //
        // i.e. a colour that saturates AT the horizon and thins toward the zenith - the haze band.
        // Three scalars plus a colour, matching adhoc_fog_color.a / adhoc_fog_atten_minscale_sky /
        // adhoc_fog_atten_sky / adhoc_fog_color.rgb one for one.
        F(10, 1, fog.AttenSky);
        F(10, 2, fog.ZenithScale);
        F(10, 3, fog.Density);
        F(11, 0, fog.Color.X); F(11, 1, fog.Color.Y); F(11, 2, fog.Color.Z);
        return buf;
    }

    /// <summary>
    /// The four values the <c>USE_ADHOC_FOG=1</c> sky program reads, already resolved into the
    /// units it wants.
    /// </summary>
    /// <param name="AttenSky">
    /// The pow() exponent on the view ray's upward component. MUST be strictly positive: the
    /// decompiled program renders pow as <c>exp2(log2(up) * atten)</c>, so at the horizon (up = 0)
    /// an exponent of 0 evaluates <c>exp2(-inf * 0)</c> = NaN and poisons the whole sky - the same
    /// trap <see cref="WildRenderingSharp.Profiles.Totk.Ubos.EnvUbo.PowExponentSlots"/> documents.
    /// <see cref="Resolve"/> is what guarantees it.
    /// </param>
    /// <param name="Color">Fog colour, ALREADY in the sky's own HDR units - the shader mixes it in raw, with no intensity multiply of its own (unlike the LUT, which it scales by Context[13].x).</param>
    public readonly record struct AdhocFog(float AttenSky, float ZenithScale, float Density, Vector3 Color);

    /// <summary>
    /// Builds the fog parameters for a palette, in the sky's own HDR units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Verified against a real capture.</b> At the game's own sky draw (eid 2016 in
    /// <c>GoodExampleTotK.rdc</c>, identified by <c>fp_c1</c> holding the sky shader's own acos
    /// constants) the Context block reads:
    /// </para>
    /// <code>
    /// [10] 4.0, 0.5, 0.3, 0.089538
    /// [11] 0.585, 1.000, 0.806, 1.0
    /// </code>
    /// <para>
    /// <c>[11].xyz</c> is <c>Prequel_MainField_Bluesky_3_Noon</c>'s own <c>FogColor</c>
    /// (0.585, 1.0, 0.806) BIT-EXACTLY - which both confirms the slot and settles that the colour
    /// goes in RAW, not normalised to a hue. <c>[10].x</c> is 4.0 = <c>adhoc_fog_atten_grd</c> (the
    /// ground pass's parameter, written but unread here) and <c>[10].z</c> is 0.3 =
    /// <c>adhoc_fog_atten_minscale_sky</c>, both matching <c>master_field.baglsky</c> exactly.
    /// </para>
    /// <para>
    /// Two things the capture CORRECTED rather than confirmed. <c>[10].y</c> is <b>0.5</b>, not the
    /// AAMP's <c>adhoc_fog_atten_sky</c> of 0.764 and not the palette's own
    /// <c>AfParam_attenuationForSky</c> of 0 - so the exponent is taken from the capture, and its
    /// real CPU-side derivation is UNKNOWN. And <c>[10].z</c> is the minscale RAW (0.3), not scaled
    /// by the density, which an earlier version of this method had wrong.
    /// </para>
    /// <para>
    /// <b>Which end is which matters, and it is what makes one formula do both looks.</b> The
    /// shader mixes from <c>[10].w</c> at the HORIZON to <c>[10].z</c> at the ZENITH. At noon
    /// 0.0895 &lt; 0.3, so the pale fog colour sits mostly overhead - a gentle high haze. Under a
    /// blood moon the palette authors <c>FogColor</c> alpha 0.6, so 0.6 &gt; 0.3 and the same mix
    /// runs the other way: saturated colour piled at the horizon, thinning upward. That is the red
    /// horizon band, and it falls straight out of the palette rather than being special-cased.
    /// </para>
    /// <para>
    /// The DENSITY (<c>[10].w</c>) is the one value whose CPU derivation is still unconfirmed. The
    /// palette's <c>FogColor</c> alpha is clearly it for palettes that author one
    /// (<c>BloodyMoon_DarknessDragon</c> authors 0.6), but noon authors 0 while the capture shows
    /// 0.089538, so there is a floor coming from somewhere this has not found. Taking the larger of
    /// the two reproduces the captured noon frame AND gives a blood moon its band; it is a
    /// reconciliation of the two pieces of evidence, not a derivation.
    /// </para>
    /// </remarks>
    /// <param name="normaliseHue">
    /// Scale <c>FogColor</c> up to full brightness before use, instead of passing it raw.
    /// <para>
    /// The capture proves RAW is what the game binds - noon's (0.585, 1.0, 0.806) went in
    /// untouched. But authored magnitudes are wildly inconsistent between palettes:
    /// <c>BloodyMoon_DarknessDragon</c>'s <c>FogColor</c> is (0.035, 0, 0.008), seventeen times
    /// darker than noon's, and on top of a blood moon's own low <c>BgDifIntensity</c> that lands
    /// the band at ~3% of the sky's peak - a near-black horizon rather than the red one. CLAUDE.md
    /// already records this same field behaving as a HUE for the sky tint for exactly that reason.
    /// So both are offered: raw is faithful to the one frame that could be measured, normalised is
    /// faithful to what the palettes look like they mean across the set.
    /// </para>
    /// </param>
    public static AdhocFog Resolve(EnvPalette palette, SkyPostFx postfx, float skyIntensity, float strength,
        bool normaliseHue = true)
    {
        // See the remarks: the palette's authored alpha where it has one, the captured noon value
        // as the floor. Never below the floor, or a palette that authors nothing loses the haze the
        // real game demonstrably has.
        float density = MathF.Max(Math.Clamp(palette.FogDensity, 0f, 1f), CapturedNoonDensity)
            * MathF.Max(0f, strength);

        // The CAPTURED value, not the AAMP's - 0.5 is what the game actually had bound at the sky
        // draw, while master_field.baglsky's adhoc_fog_atten_sky reads 0.764. Something between the
        // file and the draw transforms it and this has not found what, so the measured number wins
        // over the inferred one. Guarded strictly positive regardless: it is a pow() exponent and 0
        // decompiles to exp2(-inf * 0) = NaN at the horizon (see AttenSky's own remarks).
        float atten = CapturedAttenSky > 1e-4f ? CapturedAttenSky : 0.5f;

        // RAW, per the bit-exact capture match - but lifted into WildRenderingSharp's own sky units. The game
        // runs this shader with Context[13].x = 1 against an unnormalised LUT; WildRenderingSharp normalises
        // its LUT to NormalisedPeak and passes skyIntensity there instead, so one unit of "game
        // sky brightness" is NormalisedPeak * skyIntensity here. Handing FogColor over unscaled
        // would put a 0..1 colour against a sky whose peak is 60x smaller, i.e. a band that
        // blows out to flat colour; handing over FogColor * skyIntensity alone lands on black.
        var colour = palette.FogColor;
        if (normaliseHue)
        {
            float peak = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));
            if (peak > 1e-6f) colour /= peak;
        }
        return new AdhocFog(atten, Math.Clamp(postfx.AdhocFogAttenMinScaleSky, 0f, 1f), density,
            colour * (skyIntensity * SkyPrecomputePass.NormalisedPeak));
    }

    /// <summary>Context[10].w at the captured noon sky draw. The palette that frame authors FogColor alpha 0, so something floors it; this is that floor, measured.</summary>
    public const float CapturedNoonDensity = 0.089538f;

    /// <summary>Context[10].y at the captured sky draw - the elevation exponent. Neither the AAMP's adhoc_fog_atten_sky (0.764) nor the palette's AfParam_attenuationForSky (0), so its derivation is unknown and the measured value is used directly.</summary>
    public const float CapturedAttenSky = 0.5f;

    /// <summary>The 112-byte <c>RenderInfo</c> block (7 vec4). Confirmed against a real capture.</summary>
    /// <remarks>
    /// The capture had <c>[6] = (0.5, 0.4, 0.3, 1)</c> - the palette's authored <c>GroundColor</c>
    /// with <c>.w = 1</c>. That <c>.w</c> matters: the shader blends with
    /// <c>clamp(lut.a + [6].w, 0, 1)</c>, so 1 pins the weight to 1 and the LUT wins outright,
    /// leaving the ground colour as the fallback it is rather than something mixed in everywhere.
    /// </remarks>
    internal static byte[] BuildRenderInfo(SkyPostFx postfx, Vector3 sunWorldZUp, Vector3? fogColor = null,
        float paletteTint = 0f)
    {
        var buf = new byte[112];
        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).CopyTo(buf, slot * 16 + comp * 4);

        var br = postfx.RayleighScatteringCoeff;
        F(0, 0, br.X); F(0, 1, br.Y); F(0, 2, br.Z); F(0, 3, postfx.MieScatteringCoeff);

        Vector3 sun = ToYUp(sunWorldZUp);
        if (sun.LengthSquared() > 1e-12f) sun = Vector3.Normalize(sun);
        F(2, 0, sun.X); F(2, 1, sun.Y); F(2, 2, sun.Z);

        // The palette's own FogColor when it has one, so the colour the LUT blends toward tracks
        // the palette rather than being a single AAMP constant for every one of them.
        var g = fogColor ?? postfx.GroundColor;
        F(6, 0, g.X); F(6, 1, g.Y); F(6, 2, g.Z);
        // [6].w is the shader's own blend bias: it computes clamp(lut.a + [6].w), so 1 pins the
        // weight to 1 and the LUT wins outright (what the capture has, because the game bakes its
        // LUT per palette). WildRenderingSharp bakes one LUT from the AAMP, so at 1 the palette can never
        // change the sky's colour - dropping this below 1 lets the palette's own colour through by
        // exactly the amount asked for. 0 = the game's behaviour.
        F(6, 3, 1f - Math.Clamp(paletteTint, 0f, 1f));
        return buf;
    }

    /// <summary>Paints the sky over <paramref name="target"/> using the real program.</summary>
    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture target,
        uint bakedInscatter, ReadOnlySpan<Vector4> viewInv3Rows, float aspect, float tanHalfFovY,
        Vector3 sunWorldZUp, SkyPostFx postfx, float intensity, Vector3? fogColor = null,
        float paletteTint = 0f, AdhocFog fog = default)
    {
        if (!Available || bakedInscatter == 0)
            return;

        // Swap to the adhoc-fog program only when there is actually fog to draw. At density 0 the
        // two produce identical output (the mix weight is zero), so running the plain one then is
        // free rather than merely equivalent - and it keeps the fog variant's absence from an
        // older cache completely harmless.
        bool useFog = fog.Density > 1e-5f && _fogProgram != 0;
        uint program = useFog ? _fogProgram : _program;

        if (!_logged)
        {
            _logged = true;
            Console.WriteLine($"[SkyPostFxPass] first draw: lut={bakedInscatter} intensity={intensity:G6} " +
                $"aspect={aspect:G6} tanHalfFovY={tanHalfFovY:G6} sun={sunWorldZUp} " +
                $"adhocFog={(useFog ? $"density {fog.Density:G4}, atten {fog.AttenSky:G4}, colour {fog.Color}" : "off")}");
        }
        resources.Ubo("sky_support", SupportBufferUbo.Build(), SupportBufferUbo.BindingIndex);
        resources.Ubo("skyfx_context",
            BuildContext(viewInv3Rows, aspect * tanHalfFovY, tanHalfFovY, intensity, useFog ? fog : default), ContextBinding);
        resources.Ubo("skyfx_renderinfo", BuildRenderInfo(postfx, sunWorldZUp, fogColor, paletteTint), RenderInfoBinding);

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
