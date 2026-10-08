using System.Diagnostics;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Draws the game's own <c>agl_cloud</c> program on the procedural cloud dome (<see cref="CloudDomeMesh"/>, ported from
/// <c>Cloud::initVertex_</c>).
/// </summary>
public sealed class CloudDomePass : IDisposable
{
    const uint CommonBinding = 20;
    const uint ViewBinding = 21;
    const int ViewBytes = 256;

    readonly GL _gl;
    readonly uint _program;
    readonly uint _vao, _vbo, _ibo;
    readonly int _indexCount;
    readonly uint _baseTex, _noiseTex, _noiseBlendTex, _scatterTex;

    // Holds an already time-integrated offset, not the raw scroll speed, as the vertex shader expects.
    float _animSeconds;
    double _animBase;
    bool _dumped;
    string _lastColourKey = "";
    readonly Stopwatch _clock = Stopwatch.StartNew();

    // Clouds render into a smaller target and are composited up: the program is large, the dome
    // covers most of the screen, and the cost is pure fill rate.
    uint _cloudFbo, _cloudTex, _compositeProgram;
    int _cloudW, _cloudH;


    static readonly string CompositeFrag = GlslFiles.Load("Totk/Sky/CloudDome/Composite.frag");

    unsafe void EnsureCloudTarget(int width, int height)
    {
        if (width == _cloudW && height == _cloudH && _cloudTex != 0)
            return;
        if (_cloudTex != 0) _gl.DeleteTexture(_cloudTex);
        if (_cloudFbo == 0) _cloudFbo = _gl.GenFramebuffer();

        _cloudW = Math.Max(1, width);
        _cloudH = Math.Max(1, height);
        _cloudTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _cloudTex);
        // HDR: exposure is applied downstream.
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)_cloudW, (uint)_cloudH, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        _gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.ClampToEdge);
    }

    public unsafe CloudDomePass(GL gl, ShaderProgramCache programs, string? systemTexturesDirectory = null)
    {
        _gl = gl;

        if (!programs.Exists("agl_cloud"))
        {
            _program = 0;
            return;
        }

        _program = programs.Load("agl_cloud", patchVertex: CloudDistanceFade.PatchVertex,
            patchFragment: CloudDistanceFade.PatchFragment);

        // The shader's per-stage console binding indices collide across stages, so each block is rebound explicitly.
        bool fragCommon = _gl.BindUniformBlock(_program, "_fp_c3", CommonBinding);
        bool vertCommon = _gl.BindUniformBlock(_program, "_vp_c4", CommonBinding);
        bool vertView = _gl.BindUniformBlock(_program, "_vp_c3", ViewBinding);
        Console.WriteLine($"[CloudDomePass] real agl_cloud linked - uniform blocks rebound: " +
            $"Common(frag)={fragCommon}, Common(vert)={vertCommon}, View(vert)={vertView}");

        _gl.UseProgram(_program);
        _gl.SetSamplerUnit(_program, "fp_t_tcb_A", 0); // cBaseTexture
        _gl.SetSamplerUnit(_program, "fp_t_tcb_E", 1); // cBaseTexture_Blend
        _gl.SetSamplerUnit(_program, "fp_t_tcb_8", 2); // cNoiseTexture
        _gl.SetSamplerUnit(_program, "fp_t_tcb_C", 3); // cNoiseTexture_Blend
        _gl.SetSamplerUnit(_program, "vp_t_tcb_8", 4); // cScatterTexture (vertex stage)

        // A unit dome: the game folds the world scale into the matrices it uploads, so scaling the mesh too would apply it twice.
        Vector3[] verts = CloudDomeMesh.BuildVertices(CloudDomeMesh.NearRings);
        int[] indices = CloudDomeMesh.BuildIndices(CloudDomeMesh.NearRings);
        _indexCount = indices.Length;

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _ibo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vector3* p = verts)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(Vector3)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Vector3), (void*)0);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
        fixed (int* p = indices)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(int)), p, BufferUsageARB.StaticDraw);
        gl.BindVertexArray(0);

        // The game's own cloud masks, extracted by the preparer (not romfs assets; see res/cloud/README.md).
        // A missing mask degrades the cloud's shape instead of failing.
        _baseTex = LoadMaskOrFallback(gl, systemTexturesDirectory, "CloudBase", 0.65f);
        _noiseTex = LoadMaskOrFallback(gl, systemTexturesDirectory, "CloudNoise", 0.5f);
        // Distinct from the first noise mask: the game binds one texture to both base slots but different ones to the two noise slots.
        _noiseBlendTex = LoadMaskOrFallback(gl, systemTexturesDirectory, "CloudNoiseBlend", 0.5f);
        // Dark placeholder for when the sky bake produced nothing: the value is added to the cloud colour, so white would brighten every cloud.
        _scatterTex = CreatePlaceholderTexture(gl, 0.05f);
    }

    static EnvPalette.CloudLayer FallbackCloudLayer(CloudPostFxLayer layer) => new(
        Present: true,
        BacklightPower: layer.BacklightPower,
        ColorBackLight: layer.BacklightColor,
        ColorBase: layer.BaseColor,
        ColorHilight: layer.HilightColor,
        ColorShadow: layer.ShadowColor,
        IntensityBase: layer.BaseColorIntensity,
        IntensityHilight: layer.HilightColorIntensity,
        IntensityShadow: layer.ShadowColorIntensity);


    // The Common block read from the game's own cloud draw in a capture. The assembled block is compared against the listed
    // slots once per session so a discrepancy announces itself; the remaining slots are legitimately per-palette or per-camera.
    static readonly (int Slot, float X, float Y, float Z, float W)[] CapturedCommon =
    [
        (1, -0.0825223f, -1.03371f, 1.29866f, 4f),
        (2, 8f, 0.5f, 0.5f, 0.05f),
        (3, -0.0066f, 0.01f, 0.1f, 1.3f),
        (4, 0.05f, 1.94855f, 0.662316f, 1.2f),
        (5, 0.5f, 0.35f, 0.7f, 1.54626f),
        (7, 8f, 1.8f, 0.8f, 0.15f),
        (8, 0.15f, 0.7f, 0.95f, -1f),
        // [25].y carries the brightness gain, so it is expected to differ from the captured 2.75.
        (25, 0.393401f, float.NaN, 0.363636f, 6f),
        (26, 0.9f, 0.015f, 0.75f, 0.85f),
        (27, 8f, 0f, 0f, 0f),
        (37, 0.00333333f, 0f, 0f, 83.3301f),
        (38, 0f, 0.5f, 0f, 0f),
        (40, 3.33333e-05f, 0f, 0f, 0f),
        (43, 1f, 0.25f, 6f, 2f),
        (44, 1f, 0.25f, 0f, 0f),
        (45, 0f, 1f, 0f, 0f),
        (46, 25000f, 100f, 0f, 0f),
    ];

    static void ReportCommonBlockDrift(byte[] common)
    {
        var bad = new List<string>();
        foreach (var (slot, x, y, z, w) in CapturedCommon)
        {
            float[] want = [x, y, z, w];
            for (int c = 0; c < 4; c++)
            {
                float got = BitConverter.ToSingle(common, slot * 16 + c * 4);
                if (float.IsNaN(want[c])) continue;   // deliberately not compared
                float tol = MathF.Max(1e-4f, MathF.Abs(want[c]) * 1e-3f);
                if (MathF.Abs(got - want[c]) > tol)
                    bad.Add($"[{slot}].{"xyzw"[c]} ours={got:G6} game={want[c]:G6}");
            }
        }
        Console.WriteLine(bad.Count == 0
            ? "[CloudDomePass] Common block matches the captured game block on every compared slot."
            : $"[CloudDomePass] Common block DIFFERS from the game on {bad.Count} component(s): {string.Join("  ", bad)}");
    }


    static Vector4[] WidenFarPlane(ReadOnlySpan<Vector4> projRows, float wantedFar)
    {
        var rows = projRows.ToArray();
        if (rows.Length < 4)
            return rows;

        float a = rows[2].Z, b = rows[2].W;
        float near = MathF.Abs(a - 1f) > 1e-6f ? b / (a - 1f) : 0.1f;
        float oldFar = MathF.Abs(a + 1f) > 1e-6f ? b / (a + 1f) : wantedFar;
        if (!float.IsFinite(near) || near <= 0f)
            near = 0.1f;
        float far = MathF.Max(wantedFar, MathF.Abs(oldFar));

        rows[2] = new Vector4(rows[2].X, rows[2].Y, -(far + near) / (far - near), -2f * far * near / (far - near));
        return rows;
    }

    // Uploads an extracted single-channel mask as an R8 texture swizzled to RRRR, as the game's BC4 textures are, or a flat
    // stand-in if the file is missing.
    static unsafe uint LoadMaskOrFallback(GL gl, string? systemTexturesDirectory, string name, float fallbackValue)
    {
        if (string.IsNullOrEmpty(systemTexturesDirectory))
            return CreatePlaceholderTexture(gl, fallbackValue);

        string dataPath = Path.Combine(systemTexturesDirectory, name + ".r8");
        string dimsPath = Path.Combine(systemTexturesDirectory, name + ".dims.txt");
        if (!File.Exists(dataPath) || !File.Exists(dimsPath))
            return CreatePlaceholderTexture(gl, fallbackValue);

        string[] dims = File.ReadAllText(dimsPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (dims.Length < 2 || !int.TryParse(dims[0], out int w) || !int.TryParse(dims[1], out int h))
            return CreatePlaceholderTexture(gl, fallbackValue);

        byte[] pixels = File.ReadAllBytes(dataPath);
        if (pixels.Length < w * h)
            return CreatePlaceholderTexture(gl, fallbackValue);

        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = pixels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)w, (uint)h, 0, PixelFormat.Red, PixelType.UnsignedByte, p);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Red);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Red);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.Repeat);
        Console.WriteLine($"[CloudDomePass] loaded real {name} mask ({w}x{h}) from the system-texture cache.");
        return tex;
    }

    static unsafe uint CreatePlaceholderTexture(GL gl, float value)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        byte v = (byte)(value * 255f);
        byte[] pixel = [v, v, v, 255];
        fixed (byte* p = pixel)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.Repeat);
        return tex;
    }

    public unsafe void Run(GLResourceCache resources, RenderTargets targets, EnvPalette palette,
        CloudPostFxShared shared, CloudPostFxLayer layer,
        ReadOnlySpan<Vector4> viewRows, ReadOnlySpan<Vector4> projRows, Vector3 cameraEye, Vector3 sunWorld,
        float brightness, float exposure, bool animate,
        CloudFadeSettings fade, Vector3 skyColor, float resolutionScale, uint scatterTexture)
    {
        if (_program == 0 || !layer.IsEnable)
            return;

        var cloud = palette.Cloud0.Present ? palette.Cloud0
                  : palette.Cloud1.Present ? palette.Cloud1
                  : FallbackCloudLayer(layer);

        double now = _clock.Elapsed.TotalSeconds;
        // Scrolls at the base texture's authored speed, measured against wall-clock time so the
        // offset is right whenever a frame is drawn (the viewport only redraws on input).
        if (animate)
            _animSeconds = (float)(now - _animBase);
        else
            _animBase = now - _animSeconds;

        float scroll1X = layer.BaseTexScrollSpdX * _animSeconds;
        float scroll1Y = layer.BaseTexScrollSpdY * _animSeconds;
        float scroll2X = scroll1X;
        float scroll2Y = scroll1Y;

        // The renderer's world is Z-up, so altitude is Z.
        float skyHeightAboveCamera = MathF.Max(1f, layer.SkyHeight - cameraEye.Z);

        // Drawn at the dome's true size, which the shader's distance fades are calibrated against.
        // Depth clamping stands in for the game's disabled far clip.
        const float domeScale = 1f;

        // The game renders under its own exposure, this buffer is multiplied by the renderer's later,
        // so it is divided back out to keep the authored cloud colour at its authored magnitude.
        float skyColorGain = brightness / MathF.Max(1e-4f, exposure);
        byte[] common = BuildCommonBlock(palette, cloud, shared, layer, sunWorld,
            scroll1X, scroll1Y, scroll2X, scroll2Y, skyHeightAboveCamera, skyColorGain);

        // Dumps the assembled Common block for diffing against a capture.
        string colourSource = palette.Cloud0.Present ? "palette Cloud0"
                            : palette.Cloud1.Present ? "palette Cloud1"
                            : "master_field.baglclwd fallback";
        string colourKey = $"{colourSource}|{cloud.ColorBase}|{cloud.IntensityBase}";
        if (colourKey != _lastColourKey)
        {
            _lastColourKey = colourKey;
            Console.WriteLine($"[CloudDomePass] cloud colours from {colourSource}: " +
                $"base={cloud.ColorBase}x{cloud.IntensityBase:G4} hilight={cloud.ColorHilight}x{cloud.IntensityHilight:G4} shadow={cloud.ColorShadow}x{cloud.IntensityShadow:G4}");
        }

        if (!_dumped)
        {
            _dumped = true;
            // States the colour source outright: a palette that authors no clouds is the likeliest silent error.
            ReportCommonBlockDrift(common);
            if ((Environment.GetEnvironmentVariable("WRS_CLOUD_DUMP") ?? Environment.GetEnvironmentVariable("MARROW_CLOUD_DUMP")) is { Length: > 0 } dumpPath)
                File.WriteAllBytes(dumpPath, common);
        }
        byte[] view = BuildViewBlock(viewRows, WidenFarPlane(projRows, layer.SkyScale * 4f), cameraEye, layer, skyHeightAboveCamera, domeScale);

        // Rendered small, then composited (see _cloudTex).
        int cw = Math.Max(1, (int)(targets.Final.Width * resolutionScale));
        int ch = Math.Max(1, (int)(targets.Final.Height * resolutionScale));
        EnsureCloudTarget(cw, ch);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _cloudFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _cloudTex, 0);
        _gl.Viewport(0, 0, (uint)cw, (uint)ch);
        // Transparent black: the real blend happens once, in the composite below.
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);
        // Stands in for the game's disabled far clip.
        _gl.Enable(EnableCap.DepthClamp);
        // No blending into the cleared buffer: the game's alpha factors would zero the coverage against a transparent destination.
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.UseProgram(_program);
        resources.Ubo("cloud_common", common, CommonBinding);
        // The renderer's own distance fade; the extents are the dome's half-sizes (see CloudDistanceFade).
        resources.Ubo("cloud_fade", CloudDistanceFade.BuildUbo(
            fade.StartDistance, fade.Ramp, fade.Exponential, fade.Strength,
            new Vector3(layer.SkyScale, skyHeightAboveCamera, layer.SkyScale),
            skyColor), CloudDistanceFade.Binding);
        resources.Ubo("cloud_view", view, ViewBinding);

        BindTexture(0, _baseTex);
        BindTexture(1, _baseTex);
        BindTexture(2, _noiseTex);
        BindTexture(3, _noiseBlendTex);
        // The atmosphere's scattered light, which the fragment shader adds to the cloud colour: the sky bake's table when available.
        BindTexture(4, scatterTexture != 0 ? scatterTexture : _scatterTex);

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);

        _gl.Disable(EnableCap.DepthClamp);

        // Composite over the scene with the game's blend.
        if (_compositeProgram == 0)
            _compositeProgram = GLProgramBuilder.Build(_gl, FullscreenShaders.Vertex330, CompositeFrag, "cloud_composite");

        targets.BindColorTarget(targets.Final);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.Zero, GLEnum.OneMinusSrcAlpha);
        // The equation is stated because an earlier pass may have left a different one set.
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        _gl.UseProgram(_compositeProgram);
        _gl.BindTextureUniform(_compositeProgram, "tCloud", 0, _cloudTex);
        resources.DrawFullscreenTriangle();

        // Depth test stays off, as BackgroundPass leaves it.
        _gl.Disable(EnableCap.Blend);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    void BindTexture(int unit, uint tex)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
    }

    // The 768-byte Common block: the captured baseline with every identified slot overwritten from live data (see
    // CloudUboBaseline for what is left alone).
    internal static byte[] BuildCommonBlock(EnvPalette palette, EnvPalette.CloudLayer cloud,
        CloudPostFxShared shared, CloudPostFxLayer layer, Vector3 sunWorld,
        float scroll1X, float scroll1Y, float scroll2X, float scroll2Y,
        float skyHeightAboveCamera, float skyColorGain)
    {
        byte[] buf = CloudUboBaseline.Common();
        var u = new UniformWriter(buf);

        // Per-layer scalars from master_field.baglclwd.
        u.Set(1, 3, layer.NoiseScale1);
        u.Set(2, 0, layer.NoiseScale2);
        u.Set(2, 1, layer.NoiseDensity1);
        u.Set(2, 2, layer.NoiseDensity2);
        u.Set(2, 3, layer.EmbossWidth);
        u.Set(3, 0, layer.EmbossDensity);
        u.Set(3, 1, layer.HilightPower);
        u.Set(3, 2, layer.ShadowPower);
        u.Set(3, 3, layer.HighlightRange);
        u.Set(4, 0, layer.HighlightAmbient);
        u.Set(4, 3, layer.BacklightParam1);
        u.Set(7, 0, layer.FarUVPow);
        u.Set(7, 1, layer.FarUVMul);
        u.Set(7, 2, layer.FarDensityChgStart);
        u.Set(7, 3, layer.FarDensityChgEnd);
        u.Set(8, 0, layer.FarDensityChgPower);
        u.Set(8, 1, layer.FarAlphaChgStart);
        u.Set(8, 2, layer.FarAlphaChgEnd);
        // 8.w (the far alpha power) keeps its captured value: the authored -0.5 disagrees with the -1 the game runs.

        // Zeroed so the distance fade decides alpha; a non-zero data[45].x is a constant floor under it.
        u.Set(45, 0, 0f);
        u.Set(25, 3, layer.ScatterHeight);
        u.Set(26, 1, layer.SunOccChkSize);
        u.Set(26, 2, layer.FarDistotionChgStart);
        u.Set(26, 3, layer.FarDistotionChgEnd);

        // Shared postfx. data[25].y is the last factor the shader applies, so the gain goes there;
        // data[25].z stays the unscaled reciprocal so the fog term scales the same way.
        u.Set(25, 1, shared.CloudColorScale * skyColorGain);
        u.Set(25, 2, shared.CloudColorScale > 1e-6f ? 1f / shared.CloudColorScale : 0f);

        // Per-palette shading: xyz = colour * intensity, w = the raw intensity. Only the radiance terms
        // carry the gain; the shader reads .w structurally, so scaling it would change the cloud's shape.
        u.Set(28, cloud.ColorBase * cloud.IntensityBase);
        u.Set(28, 3, cloud.IntensityBase);
        u.Set(29, cloud.ColorHilight * cloud.IntensityHilight);
        u.Set(29, 3, cloud.IntensityHilight);
        u.Set(30, cloud.ColorShadow * cloud.IntensityShadow);
        u.Set(30, 3, cloud.IntensityShadow);
        u.Set(31, cloud.ColorBackLight); // no intensity of its own - BacklightPower is a separate term
        u.Set(31, 3, 0f);

        // Fog and scatter terms.
        u.Set(36, palette.FogColor);
        u.Set(37, 0, palette.FogEnd > 1e-6f ? 1f / palette.FogEnd : 0f);
        u.Set(41, 0, palette.ScatterFogAttenuation);
        u.Set(41, 1, palette.ScatterFogHorizontal);

        // The dome's direction matrix is a diagonal (scale, height above the camera, scale) in the
        // dome's own Y-up frame, which the shader also forwards as the surface's up-ness. The height is
        // relative to the camera so the apex stays at the authored altitude, as the capture shows.
        u.Set(32, 0, layer.SkyScale); u.Set(32, 1, 0f); u.Set(32, 2, 0f);
        u.Set(33, 0, 0f); u.Set(33, 1, skyHeightAboveCamera); u.Set(33, 2, 0f);
        u.Set(34, 0, 0f); u.Set(34, 1, 0f); u.Set(34, 2, layer.SkyScale);

        // Negated and Y-up: the slot holds the direction light travels, per the capture.
        u.Set(42, 0, -sunWorld.X); u.Set(42, 1, -sunWorld.Z); u.Set(42, 2, -sunWorld.Y);

        // Time-integrated UV scroll for the base and base-blend pairs.
        u.Set(6, 0, scroll1X); u.Set(6, 1, scroll1Y); u.Set(6, 2, scroll2X); u.Set(6, 3, scroll2Y);

        return buf;
    }

    static byte[] BuildViewBlock(ReadOnlySpan<Vector4> viewRows, ReadOnlySpan<Vector4> projRows,
        Vector3 cameraEye, CloudPostFxLayer layer, float skyHeightAboveCamera, float domeScale)
    {
        byte[] buf = new byte[ViewBytes];

        // Local Y-up unit dome to the renderer's Z-up world: scale by the sky extent, map height to Z, centre on the camera.
        float radius = layer.SkyScale * domeScale;
        float height = skyHeightAboveCamera * domeScale;
        Vector4[] model =
        [
            new Vector4(radius, 0f, 0f, cameraEye.X),
            new Vector4(0f, 0f, radius, cameraEye.Y),
            new Vector4(0f, height, 0f, cameraEye.Z),
            new Vector4(0f, 0f, 0f, 1f),
        ];
        // The view arrives as three affine rows; complete it to four to multiply.
        Vector4[] view4 = viewRows.Length == 3 ? Mat4Math.ToMat4(viewRows) : viewRows.ToArray();
        Vector4[] viewModel = Mat4Math.Multiply(view4, model);

        new UniformWriter(buf).SetRows(4, viewModel);
        new UniformWriter(buf).SetRows(8, projRows);
        new UniformWriter(buf).Set(12, 0, CloudUboBaseline.ZOffsetParam);
        return buf;
    }

    public void Dispose()
    {
        if (_program == 0)
            return;
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ibo);
        _gl.DeleteTexture(_baseTex);
        _gl.DeleteTexture(_noiseTex);
        _gl.DeleteTexture(_noiseBlendTex);
        _gl.DeleteTexture(_scatterTex);
    }
}
