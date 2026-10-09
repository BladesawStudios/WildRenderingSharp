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

    public const int MaskCount = 3;

    public static string MaskFileName(int slot) => $"CloudMask{slot}";

    readonly GL _gl;
    readonly uint _program;
    readonly uint _vao, _vbo, _ibo;
    readonly int _indexCount;
    readonly uint[] _masks = new uint[MaskCount];
    readonly uint _scatterTex;

    // Holds an already time-integrated offset, not the raw scroll speed, as the vertex shader expects.
    float _animSeconds;
    double _animBase;
    bool _dumped;
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

        // The three masks the game registers in its cloud texture table, extracted from the romfs by the preparer.
        // A missing mask degrades the cloud's shape instead of failing.
        for (int slot = 0; slot < MaskCount; slot++)
            _masks[slot] = LoadMaskOrFallback(gl, systemTexturesDirectory, MaskFileName(slot), slot == 0 ? 0.65f : 0.5f);
        // Dark placeholder for when the sky bake produced nothing: the value is added to the cloud colour, so white would brighten every cloud.
        _scatterTex = CreatePlaceholderTexture(gl, 0.05f);
    }

    internal static EnvPalette.CloudLayer FallbackCloudLayer(CloudPostFxLayer layer) => new(
        Present: true,
        BacklightPower: layer.BacklightPower,
        ColorBackLight: layer.BacklightColor,
        ColorBase: layer.BaseColor,
        ColorHilight: layer.HilightColor,
        ColorShadow: layer.ShadowColor,
        IntensityBase: layer.BaseColorIntensity,
        IntensityHilight: layer.HilightColorIntensity,
        IntensityShadow: layer.ShadowColorIntensity);


    // The Common block read from the game's own cloud draw in a capture, in its first weather. The assembled block is compared against the
    // listed slots once per session so a discrepancy announces itself; slots the weather and the clock animate (1.x to 1.z, 4.y, 4.z, 5.w) and the remaining
    // per-palette or per-camera ones are not compared.
    static readonly (int Slot, float X, float Y, float Z, float W)[] CapturedCommon =
    [
        (1, float.NaN, float.NaN, float.NaN, 4f),
        (2, 8f, 0.5f, 0.5f, 0.05f),
        (3, -0.0066f, 0.01f, 0.1f, 1.3f),
        (4, 0.05f, float.NaN, float.NaN, 1.2f),
        (5, 0.5f, 0.35f, 0.7f, float.NaN),
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


    // The camera's projection with its far plane pushed out to at least wantedFar, so the dome is not clipped.
    static Matrix4x4 WidenFarPlane(in CameraData camera, float wantedFar) =>
        Matrix4x4.CreatePerspectiveFieldOfView(2f * MathF.Atan(camera.TanHalfFovY), camera.Aspect, camera.Near, MathF.Max(wantedFar, camera.Far))
        * ClipSpace.ZeroToOneDepthToGl;

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
        // The game's textures carry a full mip chain; without one the dome's far end samples a 512x512 mask far below its
        // resolution and aliases into speckle.
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)GLEnum.TextureMaxAnisotropy, 8f);
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

    /// <summary>One cloud layer to draw: its resolved parameters and the palette colours that shade it.</summary>
    public readonly record struct Layer(CloudPostFxLayer Params, EnvPalette.CloudLayer Colours);

    /// <summary>Moves the cloud clock on (or holds it) and returns the seconds every layer's offsets and weather values are evaluated at.</summary>
    public float Advance(bool animate)
    {
        double now = _clock.Elapsed.TotalSeconds;
        // Measured against wall-clock time so the offset is right whenever a frame is drawn (the viewport only redraws on input).
        if (animate)
            _animSeconds = (float)(now - _animBase);
        else
            _animBase = now - _animSeconds;
        return _animSeconds;
    }

    /// <summary>Draws the layers in the order given, which should be far to near.</summary>
    public void Run(GLResourceCache resources, RenderTargets targets, EnvPalette palette,
        CloudPostFxShared shared, IReadOnlyList<Layer> layers, float seconds,
        in CameraData camera, Vector3 cameraEye, Vector3 sunWorld,
        float brightness, float exposure, CloudFadeSettings fade, Vector3 skyColor, float resolutionScale, uint scatterTexture)
    {
        if (_program == 0)
            return;
        foreach (var layer in layers)
            DrawLayer(resources, targets, palette, shared, layer, seconds, camera, cameraEye, sunWorld,
                brightness, exposure, fade, skyColor, resolutionScale, scatterTexture);
    }

    unsafe void DrawLayer(GLResourceCache resources, RenderTargets targets, EnvPalette palette,
        CloudPostFxShared shared, Layer drawn, float seconds,
        in CameraData camera, Vector3 cameraEye, Vector3 sunWorld,
        float brightness, float exposure, CloudFadeSettings fade, Vector3 skyColor, float resolutionScale, uint scatterTexture)
    {
        var layer = drawn.Params;
        var cloud = drawn.Colours;

        float skyHeightAboveCamera = MathF.Max(1f, layer.SkyHeight - cameraEye.Y);

        // Drawn at the dome's true size, which the shader's distance fades are calibrated against.
        // Depth clamping stands in for the game's disabled far clip.
        const float domeScale = 1f;

        // The game renders under its own exposure, this buffer is multiplied by the renderer's later,
        // so it is divided back out to keep the authored cloud colour at its authored magnitude.
        float skyColorGain = brightness / MathF.Max(1e-4f, exposure);
        byte[] common = BuildCommonBlock(palette, cloud, shared, layer, sunWorld, seconds, skyHeightAboveCamera, skyColorGain);

        if (!_dumped)
        {
            _dumped = true;
            ReportCommonBlockDrift(common);
            if ((Environment.GetEnvironmentVariable("WRS_CLOUD_DUMP") ?? Environment.GetEnvironmentVariable("MARROW_CLOUD_DUMP")) is { Length: > 0 } dumpPath)
                File.WriteAllBytes(dumpPath, common);
        }
        byte[] view = BuildViewBlock(camera.View, WidenFarPlane(camera, layer.SkyScale * 4f), cameraEye, layer, skyHeightAboveCamera, domeScale);

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

        BindTexture(0, Mask(layer.BaseTextureNo));
        BindTexture(1, Mask(layer.BaseTextureNoBlend));
        BindTexture(2, Mask(layer.NoiseTextureNo));
        BindTexture(3, Mask(layer.NoiseTextureNoBlend));
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

    uint Mask(int slot) => _masks[(uint)slot < MaskCount ? slot : 0];

    void BindTexture(int unit, uint tex)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
    }

    // The 768-byte Common block: the captured baseline with every identified slot overwritten from live data (see
    // CloudUboBaseline for what is left alone). The slot of each layer field is the order the game's own fill routine
    // (FUN_7100de5c80) writes them in.
    internal static byte[] BuildCommonBlock(EnvPalette palette, EnvPalette.CloudLayer cloud,
        CloudPostFxShared shared, CloudPostFxLayer layer, Vector3 sunWorld,
        float seconds, float skyHeightAboveCamera, float skyColorGain)
    {
        byte[] buf = CloudUboBaseline.Common();
        var u = new UniformWriter(buf);

        u.Set(0, 0, seconds);
        u.Set(0, 1, layer.Distotion);
        u.Set(0, 2, layer.Density);

        // The noise layers' texture offsets, which the game accumulates as speed times time. Noise 1's two components land in slots 0.w and 1.x,
        // noise 2's in 1.y and 1.z.
        u.Set(0, 3, layer.NoiseSpeed1X * seconds);
        u.Set(1, 0, layer.NoiseSpeed1Y * seconds);
        u.Set(1, 1, layer.NoiseSpeed2X * seconds);
        u.Set(1, 2, layer.NoiseSpeed2Y * seconds);
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

        // The alpha multiplier is pre-divided by what the threshold removes.
        u.Set(4, 1, layer.AlphaThreshold != 1f ? layer.AlphaMul / (1f - layer.AlphaThreshold) : layer.AlphaMul);
        u.Set(4, 2, layer.AlphaThreshold);
        u.Set(4, 3, layer.BacklightPower);
        u.Set(5, 0, layer.BacklightRange);
        u.Set(5, 1, layer.BacklightParam0);
        u.Set(5, 2, layer.BacklightParam1);
        u.Set(5, 3, layer.BaseTexScale);

        // The base texture's offsets, the same for both texture pairs.
        float scrollX = layer.BaseTexScrollSpdX * seconds, scrollY = layer.BaseTexScrollSpdY * seconds;
        u.Set(6, 0, scrollX); u.Set(6, 1, scrollY); u.Set(6, 2, scrollX); u.Set(6, 3, scrollY);

        u.Set(7, 0, layer.FarUVPow);
        u.Set(7, 1, layer.FarUVMul);
        u.Set(7, 2, layer.FarDensityChgStart);
        u.Set(7, 3, layer.FarDensityChgEnd);
        u.Set(8, 0, layer.FarDensityChgPower);
        u.Set(8, 1, layer.FarAlphaChgStart);
        u.Set(8, 2, layer.FarAlphaChgEnd);
        u.Set(8, 3, layer.FarAlphaChgPower);
        u.Set(27, 0, layer.FarDistotionChgPower);

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

        // Negated: the slot holds the direction light travels, per the capture.
        u.Set(42, 0, -sunWorld.X); u.Set(42, 1, -sunWorld.Y); u.Set(42, 2, -sunWorld.Z);

        return buf;
    }

    internal static byte[] BuildViewBlock(Matrix4x4 view, Matrix4x4 proj,
        Vector3 cameraEye, CloudPostFxLayer layer, float skyHeightAboveCamera, float domeScale)
    {
        byte[] buf = new byte[ViewBytes];

        // The unit dome scaled to the sky extent, with its height scaled separately, centred on the camera.
        float radius = layer.SkyScale * domeScale;
        float height = skyHeightAboveCamera * domeScale;
        var model = Matrix4x4.CreateScale(radius, height, radius) * Matrix4x4.CreateTranslation(cameraEye);

        new UniformWriter(buf).SetRows(4, GpuMatrix.Rows(model * view));
        new UniformWriter(buf).SetRows(8, GpuMatrix.Rows(proj));
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
        foreach (uint mask in _masks)
            _gl.DeleteTexture(mask);
        _gl.DeleteTexture(_scatterTex);
    }
}
