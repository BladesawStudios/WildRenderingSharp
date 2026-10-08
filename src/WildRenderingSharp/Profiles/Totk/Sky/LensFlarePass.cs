using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// The game's lens flare: <c>agl::pfx::Glare</c>'s <c>flare_filter_flare</c> program, decompiled out of
/// <c>agl_technique_pfx.sharcb</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its whole interface is one sampler (<c>cSrc</c>) and one 192-byte <c>RegisterUBO</c>, so the block was recoverable from the decompiled math:
/// </para>
/// <code>
/// vertex:    ghost = (0.5 - uv) * C[0].x          a step from this pixel toward screen centre
/// gl_Position.xy = in_attr0.xy * 2.0   half-unit quad, same convention as the sky pass
/// fragment:  sum  = src(uv) + src(uv + g*2) + src(uv + g*4) + src(uv + g*6)
/// halo = src(uv + normalize(g) * C[1].w * 2)
/// out  = (sum + halo * C[1].xyz) * C[3].xyz
/// </code>
/// <para>
/// So <c>C[0].x</c> is the ghost spacing, <c>C[1].xyz</c> and <c>.w</c> the halo's tint and radius, and <c>C[3].xyz</c> the overall intensity. The ghost count is
/// the <c>GHOST_NUM</c> macro baked in at extraction (4), hence the unrolled taps.
/// </para>
/// <para>
/// Stepping toward the screen centre and past it lands on the light's reflection through the centre, which is where a lens puts its ghosts. So <c>cSrc</c> must
/// be a bright-pass of the scene, not the scene: the raw image would drag ordinary geometry into the ghosts. This pass owns that threshold rather than borrowing
/// <see cref="BloomPass"/>'s intermediates, so their tunings cannot pull against each other.
/// </para>
/// <para>
/// Not implemented: the glare streak chain. <c>glare_filter_blur</c> is extracted (<c>agl_glare_filter_blur0</c> and <c>1</c>) but is a multi-pass separable blur
/// driven from <c>glare_filter_seed</c>'s own block, which is undecoded. It produces the anamorphic streaks, not the ghosts.
/// </para>
/// </remarks>
public sealed class LensFlarePass : IDisposable
{
    /// <summary>Clear of every binding the real program declares.</summary>
    const uint RegisterBinding = 24;

    readonly GL _gl;
    readonly uint _program;
    readonly uint _brightProgram;
    readonly uint _blurProgram;
    readonly uint _vao, _vbo;
    // Ping-pong pair at quarter resolution: [0] receives the bright-pass, the blur bounces
    // between the two and always ends back in [0], which is what the flare samples.
    readonly uint[] _srcTex = new uint[2], _srcFbo = new uint[2];
    int _brightW, _brightH;

    /// <summary>Separable blur iterations (each = one horizontal + one vertical pass).</summary>
    const int BlurIterations = 3;

    public bool Available => _program != 0;
    bool _logged;

    const string BrightFrag = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tSrc;
        uniform float uThreshold;
        uniform float uExposure;
        uniform vec2 uSrcTexel;
        uniform sampler2D tDepth;
        uniform int uSkyOnly;
        out vec4 oCol;
        vec3 bright(vec2 uv)
        {
            // Sky-only source: the game's flare is a sun effect (gated on effect_sun_occlusion_cs), so geometry never throws one, whereas an emissive
            // weapon past any threshold did here. GBufferDepth is in the G-buffer's flipped orientation, hence 1 - v; far-plane depth is sky.
            if (uSkyOnly != 0 && texture(tDepth, vec2(uv.x, 1.0 - uv.y)).r < 0.99999)
                return vec3(0.0);
            // Thresholded in display space (after exposure) with the excess compressed below 1 on the brightest channel, hue kept: unbounded excess let one emissive object bury the frame in ghosts.
            vec3 e = max(texture(tSrc, uv).rgb * uExposure - uThreshold, 0.0);
            float m = max(e.r, max(e.g, e.b));
            return m > 0.0 ? e * (1.0 / (1.0 + m)) : vec3(0.0);
        }
        void main()
        {
            // Four bilinear taps one source texel off-centre cover the whole 4x4 footprint of a quarter-res texel, so a thin bright edge cannot slip between samples and shimmer.
            oCol = vec4(0.25 * (bright(vUV + uSrcTexel * vec2(-1.0, -1.0))
                              + bright(vUV + uSrcTexel * vec2( 1.0, -1.0))
                              + bright(vUV + uSrcTexel * vec2(-1.0,  1.0))
                              + bright(vUV + uSrcTexel * vec2( 1.0,  1.0))), 1.0);
        }
        """;

    // 9-tap Gaussian folded into 5 bilinear fetches (the standard linear-sampling offsets).
    const string BlurFrag = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tSrc;
        uniform vec2 uStep;
        out vec4 oCol;
        void main()
        {
            vec3 c = texture(tSrc, vUV).rgb * 0.2270270270;
            c += texture(tSrc, vUV + uStep * 1.3846153846).rgb * 0.3162162162;
            c += texture(tSrc, vUV - uStep * 1.3846153846).rgb * 0.3162162162;
            c += texture(tSrc, vUV + uStep * 3.2307692308).rgb * 0.0702702703;
            c += texture(tSrc, vUV - uStep * 3.2307692308).rgb * 0.0702702703;
            oCol = vec4(c, 1.0);
        }
        """;

    public unsafe LensFlarePass(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;
        if (!programs.Exists("agl_flare_filter_flare"))
        {
            Console.WriteLine("[LensFlarePass] agl_flare_filter_flare not in the shader cache - disabled.");
            return;
        }

        _program = programs.Load("agl_flare_filter_flare");
        // RegisterUBO is at location 0 in both stages, so it decompiles to vp_c3 and fp_c3: one block under two names once linked, the same collision CloudDomePass and SkyPostFxPass correct.
        _gl.BindUniformBlock(_program, "_vp_c3", RegisterBinding);
        _gl.BindUniformBlock(_program, "_fp_c3", RegisterBinding);
        _gl.UseProgram(_program);
        _gl.SetSamplerUnit(_program, "fp_t_tcb_8", 0); // cSrc

        _brightProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, BrightFrag, "lens_flare_bright");
        _blurProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, BlurFrag, "lens_flare_blur");

        // in_attr0 is the half-unit position (the vertex does gl_Position.xy = in_attr0.xy * 2.0) and in_attr1 the uv, unlike the sky pass's position-only quad.
        Span<float> quad =
        [
            -0.5f, -0.5f, 0f, 0f,
             0.5f, -0.5f, 1f, 0f,
            -0.5f,  0.5f, 0f, 1f,
             0.5f,  0.5f, 1f, 1f,
        ];
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.BindVertexArray(0);

        Console.WriteLine("[LensFlarePass] real agl_flare_filter_flare linked (4 ghosts + halo).");
    }

    internal static byte[] BuildRegisterUbo(float ghostSpacing, Vector3 haloTint, float haloRadius, Vector3 intensity)
    {
        var buf = new byte[192];
        var u = new UniformWriter(buf);
        u.Set(0, 0, ghostSpacing);
        u.Set(1, 0, haloTint.X); u.Set(1, 1, haloTint.Y); u.Set(1, 2, haloTint.Z); u.Set(1, 3, haloRadius);
        u.Set(3, 0, intensity.X); u.Set(3, 1, intensity.Y); u.Set(3, 2, intensity.Z);
        return buf;
    }

    public readonly record struct Params(
        float Threshold, float GhostSpacing, Vector3 HaloTint, float HaloRadius, Vector3 Intensity, float Exposure, bool SkyOnly);

    /// <summary>Adds the flare to <paramref name="hdr"/>, reading it as its own source.</summary>
    public unsafe void Run(GLResourceCache resources, RenderTargets targets, GpuTexture hdr, Params p)
    {
        if (!Available)
            return;

        // Quarter resolution and blurred: each ghost is the source magnified (about 3x at the default spacing), so an unblurred source would give giant hard-edged
        // copies instead of soft blobs. The game's flare runs on a small blurred buffer too.
        EnsureSource(Math.Max(1, hdr.Width / 4), Math.Max(1, hdr.Height / 4));

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _srcFbo[0]);
        _gl.Viewport(0, 0, (uint)_brightW, (uint)_brightH);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.UseProgram(_brightProgram);
        _gl.BindTextureUniform(_brightProgram, "tSrc", 0, hdr.Handle);
        _gl.SetFloat(_brightProgram, "uThreshold", p.Threshold);
        _gl.SetFloat(_brightProgram, "uExposure", MathF.Max(p.Exposure, 1e-4f));
        _gl.BindTextureUniform(_brightProgram, "tDepth", 1, targets.GBufferDepth.Handle);
        _gl.Uniform1(_gl.GetUniformLocation(_brightProgram, "uSkyOnly"), p.SkyOnly ? 1 : 0);
        _gl.SetVec2(_brightProgram, "uSrcTexel", new Vector2(1f / hdr.Width, 1f / hdr.Height));
        resources.DrawFullscreenTriangle();

        _gl.UseProgram(_blurProgram);
        var texel = new Vector2(1f / _brightW, 1f / _brightH);
        for (int i = 0; i < BlurIterations; i++)
        {
            // Widen each iteration so a few cheap passes reach a large radius.
            float spread = 1f + i;
            BlurInto(resources, 1, _srcTex[0], new Vector2(texel.X * spread, 0f));
            BlurInto(resources, 0, _srcTex[1], new Vector2(0f, texel.Y * spread));
        }

        if (!_logged)
        {
            _logged = true;
            Console.WriteLine($"[LensFlarePass] first draw: threshold={p.Threshold:G4} spacing={p.GhostSpacing:G4} " +
                $"haloRadius={p.HaloRadius:G4} intensity={p.Intensity}");
        }

        // The source is in display units but the flare is added before exposure, so exposure is divided back out of the intensity (as DeferredResolvePass does for emission).
        resources.Ubo("flare_register",
            BuildRegisterUbo(p.GhostSpacing, p.HaloTint, p.HaloRadius,
                p.Intensity / MathF.Max(p.Exposure, 1e-4f)), RegisterBinding);

        // Additive over the HDR buffer before exposure and tonemap: a flare is light the lens adds, so it belongs in linear space.
        targets.BindColorTarget(hdr);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.One, GLEnum.One, GLEnum.Zero, GLEnum.One);
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        _gl.UseProgram(_program);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _srcTex[0]);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
    }

    void BlurInto(GLResourceCache resources, int target, uint source, Vector2 step)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _srcFbo[target]);
        _gl.BindTextureUniform(_blurProgram, "tSrc", 0, source);
        _gl.SetVec2(_blurProgram, "uStep", step);
        resources.DrawFullscreenTriangle();
    }

    unsafe void EnsureSource(int width, int height)
    {
        if (width == _brightW && height == _brightH && _srcTex[0] != 0)
            return;
        _brightW = width;
        _brightH = height;
        for (int i = 0; i < 2; i++)
        {
            if (_srcTex[i] != 0) _gl.DeleteTexture(_srcTex[i]);
            if (_srcFbo[i] == 0) _srcFbo[i] = _gl.GenFramebuffer();
            _srcTex[i] = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _srcTex[i]);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            // Clamped to a black border, not to edge: the ghost taps walk past the far side of the screen, and clamp-to-edge would smear the border pixel into a ghost.
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);
            Span<float> border = [0f, 0f, 0f, 0f];
            fixed (float* b = border)
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, b);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _srcFbo[i]);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _srcTex[i], 0);
        }
    }

    public void Dispose()
    {
        if (_program != 0) _gl.DeleteProgram(_program);
        if (_brightProgram != 0) _gl.DeleteProgram(_brightProgram);
        if (_blurProgram != 0) _gl.DeleteProgram(_blurProgram);
        for (int i = 0; i < 2; i++)
        {
            if (_srcTex[i] != 0) _gl.DeleteTexture(_srcTex[i]);
            if (_srcFbo[i] != 0) _gl.DeleteFramebuffer(_srcFbo[i]);
        }
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
    }
}
