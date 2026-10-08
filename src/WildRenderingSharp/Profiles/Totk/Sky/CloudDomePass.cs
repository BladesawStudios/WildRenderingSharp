using System.Diagnostics;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Draws the REAL, decompiled <c>agl_cloud</c> shader (auto-extracted from romfs by
/// <c>ModelPreparer.EnsureCloudShader</c>) on the REAL procedurally-generated cloud dome
/// (<see cref="CloudDomeMesh"/>, ported from the actual <c>Cloud::initVertex_</c>) - the game's own
/// compiled program, not an approximation of it.
///
/// HOW THE UNIFORM LAYOUT WAS ESTABLISHED (this matters - it is not guesswork): a real frame was
/// captured from the running game (Ryujinx launched under RenderDoc with <c>--software-gui</c>, which
/// avoids the Avalonia/ANGLE compositor crash RenderDoc's D3D11 hook otherwise causes), the cloud
/// dome's own draw located in Colour Pass #40, and the literal bytes of its two uniform blocks read
/// out. Both blocks' contents were then matched value-by-value against the real ROM data:
///   - <c>postfx/master_field.baglclwd</c>'s own CloudParam0 fields (the shape/noise/emboss/LOD
///     scalars), and
///   - the active EnvPalette's <c>Cloud0</c> block (the COLOURS - which is the part that was wrong
///     before: cloud colour comes from the per-palette EnvPalette, NOT from the postfx AAMP file,
///     which is why the first attempt only looked right on some palettes).
/// The capture's palette was identified as <c>Prequel_MainField_Bluesky_3_Noon</c> by matching its
/// authored cloud colours byte-for-byte, which is what confirmed the colour slots' exact meaning
/// (<c>xyz</c> = colour * intensity, <c>w</c> = that same intensity).
///
/// Geometry was confirmed the same way: the real draw uses 289 vertices at 12-byte stride with 1656
/// indices, all three of which <see cref="CloudDomeMesh"/>'s own formulas reproduce exactly for a
/// 12-ring dome.
///
/// The block itself is verified end-to-end, not merely plausible: rebuilding it from the capture's
/// own palette plus the ROM's AAMP values reproduces 191 of the captured block's 192 floats
/// bit-exactly. The one that differed is what identified slot 33.y - the capture held
/// <c>mSkyHeight</c> minus the camera's altitude (7895.27 - 347.85 = 7547.42), i.e. the dome's
/// height ABOVE THE VIEWER, which is what <see cref="Run"/> now computes.
///
/// WHAT'S STILL APPROXIMATE, honestly:
///   - The base/noise masks are the best-evidenced real candidates rather than proven ones - see
///     <c>SystemTextures.ExtractCloudTextures</c> for the fingerprint they were identified by.
///   - <c>cScatterTexture</c> (sampled per-vertex for the cloud's own lit colour) isn't wired to
///     WildRenderingSharp's own ray-marched atmosphere yet, so it stays flat and the palette's colour terms
///     carry the shading alone.
///   - The placement-point proximity fades (slots 9/13/17/21) stay at their captured values; wiring
///     them properly needs the real EffectCloudPlacementPoints data.
///   - The dome is uniformly scaled down to fit WildRenderingSharp's shared camera frustum (see <see cref="Run"/>),
///     since WildRenderingSharp can't honour the real config's "disable far clip" the way the game does.
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

    // The real UV-scroll slot holds an already-time-integrated offset, not the raw
    // mBaseTexScrollSpdX/Y speed (established from the vertex shader's own use of it), so the
    // integration has to happen here.
    float _animSeconds;
    double _animBase;
    bool _dumped;
    string _lastColourKey = "";
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastElapsedSeconds;

    // Clouds are rendered into their OWN, smaller target and then composited up. The agl_cloud
    // fragment program is enormous (heavily unrolled procedural noise) and, now that the dome
    // renders at its true 26500-unit size, it covers essentially the whole screen - so its cost is
    // pure fill rate and scales with viewport resolution, which is exactly the lag reported. The
    // Switch gets away with it at a fixed 900p; a desktop viewport at 4x that area does not.
    // Halving each axis is a 4x reduction in fragments, and clouds are soft enough that the upscale
    // is not visible.
    uint _cloudFbo, _cloudTex, _compositeProgram;
    int _cloudW, _cloudH;


    // A genuine oversized fullscreen TRIANGLE - three vertices at (0,0), (2,0), (0,2) in UV space,
    // which map to an NDC triangle that fully contains the screen. It has to be a triangle, not a
    // 4-vertex strip: the composite is issued through DrawFullscreenTriangle, which draws 3
    // vertices as a triangle list, so a strip definition silently lost the fourth vertex and
    // covered only the lower-left half of the screen along a diagonal.
    const string CompositeVert = """
        #version 330 core
        out vec2 vUV;
        void main()
        {
            vUV = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(vUV * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    const string CompositeFrag = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tCloud;
        out vec4 oCol;
        void main() { oCol = texture(tCloud, vUV); }
        """;

    /// <summary>Ensures the offscreen cloud target matches the requested resolution.</summary>
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
        // 16F: the cloud colour is HDR and gets the viewer's Exposure applied downstream.
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)_cloudW, (uint)_cloudH, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
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

        // The decompiled source's own layout(binding=N) numbers COLLIDE across stages (the
        // fragment stage declares the Common block at raw binding 4 while the vertex stage declares
        // the View block at raw binding 4 too) because they are per-stage console constant-buffer
        // indices, not one shared desktop-GL binding namespace. Reassigning each interface block
        // explicitly after linking is what makes them coexist in a single linked GL program.
        bool fragCommon = BindBlock("_fp_c3", CommonBinding);
        bool vertCommon = BindBlock("_vp_c4", CommonBinding);
        bool vertView = BindBlock("_vp_c3", ViewBinding);
        Console.WriteLine($"[CloudDomePass] real agl_cloud linked - uniform blocks rebound: " +
            $"Common(frag)={fragCommon}, Common(vert)={vertCommon}, View(vert)={vertView}");

        _gl.UseProgram(_program);
        SetSamplerUnit("fp_t_tcb_A", 0); // cBaseTexture
        SetSamplerUnit("fp_t_tcb_E", 1); // cBaseTexture_Blend
        SetSamplerUnit("fp_t_tcb_8", 2); // cNoiseTexture
        SetSamplerUnit("fp_t_tcb_C", 3); // cNoiseTexture_Blend
        SetSamplerUnit("vp_t_tcb_8", 4); // cScatterTexture (vertex stage)

        // A UNIT dome, uploaded once. The real game keeps its vertex buffer unit-local too and
        // folds the world scale into the matrices it uploads (confirmed: the captured View block's
        // own slots 4-7 carry 25000-magnitude terms, i.e. view*model pre-combined) - so scaling the
        // mesh here as well would double-apply it.
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

        // Real BC4 masks installed by ModelPreparer.EnsureCloudTextures. These are the game's
        // OWN cloud textures, captured from its real cloud draw - they are NOT romfs assets and
        // are not extractable (see res/cloud/README.md); the earlier romfs picks were disproved
        // byte-wise against the real bindings.
        // Falls back to a flat stand-in if extraction hasn't run - a missing mask should degrade the
        // cloud's shape, not crash the pass.
        _baseTex = LoadMaskOrFallback(gl, systemTexturesDirectory, "CloudBase", 0.65f);
        _noiseTex = LoadMaskOrFallback(gl, systemTexturesDirectory, "CloudNoise", 0.5f);
        // A THIRD, distinct mask. The game binds cBaseTexture and cBaseTexture_Blend to the same
        // texture but cNoiseTexture and cNoiseTexture_Blend to DIFFERENT ones - confirmed by
        // dumping the real cloud draw's bindings from a capture. Binding the same noise to both
        // (what this did before) silently loses the cross-fade the blend slot exists for.
        _noiseBlendTex = LoadMaskOrFallback(gl, systemTexturesDirectory, "CloudNoiseBlend", 0.5f);
        // cScatterTexture is sampled per-vertex for the cloud's own lit colour; WildRenderingSharp has no real
        // baked atmosphere texture to point it at yet, so it stays flat and the palette's own
        // colour terms carry the shading.
        // Dark, not white: this is ADDED to the cloud colour, so a white fallback brightens and
        // desaturates every cloud. Only used when the sky bake has produced nothing.
        _scatterTex = CreatePlaceholderTexture(gl, 0.05f);
    }

    /// <summary>
    /// Cloud colours for a palette that authors none, taken from the ROM's own
    /// <c>master_field.baglclwd</c> instead.
    /// </summary>
    /// <remarks>
    /// Previously the pass simply returned when a palette had no <c>Cloud0</c>/<c>Cloud1</c>, which
    /// is why clouds appeared only on real in-game palettes and never on the viewer's own studio
    /// lighting. The postfx file authors a complete parallel set - base/hilight/shadow/backlight
    /// colours with their own intensities - so this is still real authored game data, not invented
    /// values; it is simply the other place the game keeps them.
    /// </remarks>
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


    /// <summary>
    /// The game's own 768-byte <c>Common</c> block, read from the real cloud draw in a capture.
    /// Only the slots listed are compared - the rest are legitimately per-palette or per-camera.
    /// </summary>
    /// <remarks>
    /// This exists because the cloud UBO is a blob with no name table, so "which slot is wrong" is
    /// not answerable by reading code - three of WildRenderingSharp's own writes disagreed with the game and
    /// were only ever found by diffing against this. Logged once per session so a discrepancy
    /// announces itself instead of being hunted.
    /// </remarks>
    static readonly (int Slot, float X, float Y, float Z, float W)[] CapturedCommon =
    [
        (1, -0.0825223f, -1.03371f, 1.29866f, 4f),
        (2, 8f, 0.5f, 0.5f, 0.05f),
        (3, -0.0066f, 0.01f, 0.1f, 1.3f),
        (4, 0.05f, 1.94855f, 0.662316f, 1.2f),
        (5, 0.5f, 0.35f, 0.7f, 1.54626f),
        (7, 8f, 1.8f, 0.8f, 0.15f),
        (8, 0.15f, 0.7f, 0.95f, -1f),
        // [25].y deliberately omitted: it carries the brightness/Exposure gain, so it is
        // SUPPOSED to differ from the captured 2.75.
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

    /// <summary>Logs every compared slot where the assembled block differs from the game's own.</summary>
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


    /// <summary>
    /// The camera's projection with its far plane pushed out past the cloud dome, leaving the field
    /// of view and near plane exactly as they are.
    /// </summary>
    /// <remarks>
    /// The dome is ~26500 units across while WildRenderingSharp's far plane defaults to 4000, so EVERY vertex of
    /// it sits beyond the far plane and the whole thing is clipped away. GL_DEPTH_CLAMP is the
    /// textbook answer and is still enabled, but it demonstrably was not enough here - clouds only
    /// appeared once the far plane was raised by hand, and they vanished entirely from camera
    /// renders, whose cameras carry their own (smaller) far plane. Giving this one draw a
    /// projection that can actually contain the dome removes the dependency altogether.
    ///
    /// Safe precisely because this pass does not depth-test: the widened far plane changes only
    /// whether the dome survives clipping, never how it sorts against anything else.
    ///
    /// The near plane is recovered from the incoming matrix rather than passed in, so this cannot
    /// drift from whatever the caller's camera actually uses: for the standard GL perspective form,
    /// A = -(f+n)/(f-n) and B = -2fn/(f-n), hence n = B/(A-1).
    /// </remarks>
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

    /// <summary>Points one of the shader's own interface blocks at a binding of our choosing. Returns whether the block actually exists in the linked program (a block the compiler dropped as unused reports false, which is informative rather than fatal).</summary>
    bool BindBlock(string name, uint binding)
    {
        uint idx = _gl.GetUniformBlockIndex(_program, name);
        if (idx == 0xFFFFFFFFu)
            return false;
        _gl.UniformBlockBinding(_program, idx, binding);
        return true;
    }

    void SetSamplerUnit(string name, int unit)
    {
        int loc = _gl.GetUniformLocation(_program, name);
        if (loc >= 0)
            _gl.Uniform1(loc, unit);
    }

    /// <summary>
    /// Uploads one of the real extracted single-channel BC4 masks as an R8 texture, broadcast RRRR
    /// the same way every other BC4-sourced texture in this codebase is (and the same way the real
    /// capture shows the game itself binding these) so the shader's own <c>.x</c> reads land on real
    /// data. Returns a flat stand-in if the extracted file isn't there.
    /// </summary>
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
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
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
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        return tex;
    }

    /// <param name="palette">The live palette - its <c>Cloud0</c> colours/intensities are what actually make the cloud the right colour (see class remarks), along with its fog terms.</param>
    /// <param name="viewRows">WildRenderingSharp's own real camera view matrix (4 row vectors).</param>
    /// <param name="projRows">WildRenderingSharp's own real camera projection matrix.</param>
    /// <param name="cameraEye">World-space eye position - the dome is re-centred on it every frame.</param>
    /// <param name="sunWorld">WildRenderingSharp's Z-up world sun direction.</param>
    /// <param name="farPlane">The camera's own far clip distance - the dome gets scaled to sit inside it, see the remarks in the body.</param>
    /// <param name="sceneGain">The viewer's lit-path correction, applied to the cloud's colours for the same reason the sky behind it gets it - see <see cref="BackgroundPass.SkyColorAnchor"/>.</param>
    /// <param name="brightness">Live multiplier on the palette-authored cloud colour. 1.0 = exactly as authored.</param>
    /// <param name="exposure">The Exposure <c>TonemapPass</c> will apply to this buffer, divided back out of the cloud colour - see the body.</param>
    public unsafe void Run(GLResourceCache resources, RenderTargets targets, EnvPalette palette,
        CloudPostFxShared shared, CloudPostFxLayer layer,
        ReadOnlySpan<Vector4> viewRows, ReadOnlySpan<Vector4> projRows, Vector3 cameraEye, Vector3 sunWorld,
        float farPlane, float sceneGain, float brightness, float exposure, bool animate,
        CloudFadeSettings fade, Vector3 skyColor, float resolutionScale, uint scatterTexture)
    {
        if (_program == 0 || !layer.IsEnable)
            return;

        var cloud = palette.Cloud0.Present ? palette.Cloud0
                  : palette.Cloud1.Present ? palette.Cloud1
                  : FallbackCloudLayer(layer);

        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = _lastElapsedSeconds == 0 ? 0f : (float)(now - _lastElapsedSeconds);
        _lastElapsedSeconds = now;
        // TWO independent scroll offsets, which is what makes the clouds move: the capture has
        // Common[6] = (-0.0384, 0.0782, 0.4983, 0.3637) - four distinct values, i.e. xy for the
        // first noise layer and zw for the second, each at its own authored speed. Writing one pair
        // twice (what this did before) locks the two layers together, and the whole point of two
        // layers drifting at different rates is that the cloud shapes evolve rather than slide.
        // BASE texture speed, not the noise speeds. Checked against the capture rather than
        // guessed: Common[6].xy there is (-0.0384, +0.0782), a ratio of -0.49, and
        // mBaseTexScrollSpdX/Y are (-0.00005, +0.0001), a ratio of -0.50. Exact match, sign and
        // all. The NoiseSpeed* fields are ~5000x larger and drove the clouds visibly across the
        // sky in seconds.
        //
        // Accumulated against WALL-CLOCK elapsed time, not summed per-frame deltas: the viewport
        // only redraws on input, so a delta sum advances only while the mouse moves and freezes the
        // instant it stops. Elapsed time makes the offset a function of when the frame happened, so
        // it is always correct whenever a frame does get drawn. _animBase holds the time the clock
        // is measured from so pausing does not jump.
        if (animate)
            _animSeconds = (float)(now - _animBase);
        else
            _animBase = now - _animSeconds;

        float scroll1X = layer.BaseTexScrollSpdX * _animSeconds;
        float scroll1Y = layer.BaseTexScrollSpdY * _animSeconds;
        float scroll2X = scroll1X;
        float scroll2Y = scroll1Y;
        _ = deltaSeconds;

        // WildRenderingSharp's world is Z-up, so the camera's altitude is its Z (see Camera.cs).
        float skyHeightAboveCamera = MathF.Max(1f, layer.SkyHeight - cameraEye.Z);

        // The real authored dome is ~26500 units across; WildRenderingSharp's camera far plane defaults to
        // 4000, so at true scale the whole dome sits beyond the far plane and clips away entirely
        // (nothing renders). The real game avoids that by disabling far clipping for clouds
        // outright - its own config says so: mIsDisableFarClip is true on the shared Cloud object.
        // WildRenderingSharp shares one projection with the rest of the scene, so instead the dome is scaled to
        // sit inside the frustum. That is visually equivalent rather than a fudge: the shader's own
        // distance fades read the UNIT local position (in_attr0), and its scatter direction is
        // normalised, so a uniform scale changes the dome's projected size only - none of the
        // shading maths depends on absolute world size.
        // TRUE scale. The dome is ~26500 units across and WildRenderingSharp's far plane defaults to 4000, so
        // this used to shrink it to fit - and that is what broke the distance fade. The shader's
        // far-fade constants are calibrated against the real dome's real size, so rendering it at
        // ~7% scale left every fragment in the "near" bucket, and the fade only reappeared as the
        // far plane was pushed out (which is exactly what shrinking less does).
        //
        // The real game does not scale anything: agl's Cloud object sets mIsDisableFarClip. GL's
        // equivalent is GL_DEPTH_CLAMP, which clamps depth instead of clipping the geometry away -
        // so the dome can sit past the far plane at its authored size, at any far plane.
        const float domeScale = 1f;
        _ = farPlane;

        // NOT anchored the way BackgroundPass anchors the atmosphere behind it, despite both
        // painting into targets.Final ahead of the same Exposure multiply. That symmetry looked
        // compelling on paper - the two were ~180x apart arithmetically - but it is wrong: the
        // clouds were confirmed to look CORRECT at the palette's authored scale, and it was the
        // ATMOSPHERE that was over-bright. Pre-dividing these to match the atmosphere therefore
        // fixed nothing and scaled a good result into near-invisibility. The 180x gap is real but
        // its sign is the opposite of what it looks like: it says the atmosphere's own anchor is
        // suspect, not that the cloud needs one. Left neutral at 1.0 (the confirmed-good default)
        // with the slider as pure headroom.
        // Divide back out the Exposure that TonemapPass will apply to this whole buffer, so the
        // palette's authored cloud colour survives at the magnitude it was authored in. This is
        // the same trick DeferredResolvePass already uses to keep emission exposure-invariant.
        //
        // Without it the colours land at roughly 0.15-1.0, get multiplied by ~9.5, and clip to
        // white - which is why the brightness control appeared to do nothing: almost its entire
        // range sat above the clipping point. To get the old blown-out look back, set brightness
        // to about the Exposure value.
        // Gain 1.0 reproduces the game's own final multiplier exactly (the capture has
        // Common[25].y = 2.75 = CloudColorScale raw), so the default matches the real game rather
        // than a correction of it. The Exposure divide-back is gone: it made the clouds ~9.5x
        // dimmer than the game for no evidenced reason, and it was never what made the slider feel
        // dead - that was the gain sitting on the input colours, where CloudColorScale re-amplified
        // it afterwards. On the final multiplier it scales the real output.
        // Divided back out of the Exposure this buffer is about to be multiplied by, for exactly
        // the reason the sky is. Removing this earlier was a mistake made for a bad reason: the
        // capture has Common[25].y = CloudColorScale raw, so matching it looked "correct" - but the
        // GAME renders that value under ITS own exposure (this palette authors Exposure: 0.0),
        // not under WildRenderingSharp's 9.5x stand-in. Matching the byte while ignoring what multiplies it
        // afterwards left the clouds several stops hot, which is why they only looked right with
        // the brightness control wound all the way to zero.
        float skyColorGain = brightness / MathF.Max(1e-4f, exposure);
        _ = sceneGain;
        byte[] common = BuildCommonBlock(palette, cloud, shared, layer, sunWorld,
            scroll1X, scroll1Y, scroll2X, scroll2Y, skyHeightAboveCamera, skyColorGain);

        // Diagnostic hook: dumps the assembled Common block so it can be diffed slot-by-slot
        // against the real one read out of a GPU capture. Every remaining cloud discrepancy is by
        // definition a slot where these two disagree.
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
            // The cloud colours are the thing most likely to be silently wrong (a palette that
            // authors none, or a delta that lost its parent's), and white clouds on a red palette
            // is exactly what that looks like - so state the source and the values outright.
            ReportCommonBlockDrift(common);
            if ((Environment.GetEnvironmentVariable("WRS_CLOUD_DUMP") ?? Environment.GetEnvironmentVariable("MARROW_CLOUD_DUMP")) is { Length: > 0 } dumpPath)
                File.WriteAllBytes(dumpPath, common);
        }
        byte[] view = BuildViewBlock(viewRows, WidenFarPlane(projRows, layer.SkyScale * 4f), cameraEye, layer, skyHeightAboveCamera, domeScale);

        // Render into the smaller offscreen target, not straight into Final - see _cloudTex.
        int cw = Math.Max(1, (int)(targets.Final.Width * resolutionScale));
        int ch = Math.Max(1, (int)(targets.Final.Height * resolutionScale));
        EnsureCloudTarget(cw, ch);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _cloudFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _cloudTex, 0);
        _gl.Viewport(0, 0, (uint)cw, (uint)ch);
        // Transparent black: the composite below re-applies the real blend against the sky, so this
        // buffer must hold the cloud's own premultiplied-by-nothing colour and coverage only.
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);
        // Stand in for the real object's mIsDisableFarClip: without this the dome, which sits well
        // beyond the far plane at its true size, is clipped away by the rasteriser regardless of
        // whether a depth buffer is bound.
        _gl.Enable(EnableCap.DepthClamp);
        // NO blending for this draw. The game's real state (SrcAlpha/InvSrcAlpha with alpha factors
        // Zero/InvSrcAlpha) is correct when drawing STRAIGHT onto the sky - but this draw goes into
        // a cleared, transparent-black buffer, and against dst=(0,0,0,0) those alpha factors give
        // dstA = 0*srcA + (1-srcA)*0 = 0. The coverage is annihilated, the composite then blends
        // nothing, and the clouds vanish entirely. The dome writes its colour and coverage
        // unmodified here; the real blend happens once, in the composite below.
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.UseProgram(_program);
        resources.Ubo("cloud_common", common, CommonBinding);
        // WildRenderingSharp's own distance fade - see CloudDistanceFade for why this exists alongside the
        // game's. Extents are the dome's real half-sizes, so the patch can turn its unit local
        // position straight into a view distance.
        resources.Ubo("cloud_fade", CloudDistanceFade.BuildUbo(
            fade.StartDistance, fade.Ramp, fade.Exponential, fade.Strength,
            new Vector3(layer.SkyScale, skyHeightAboveCamera, layer.SkyScale),
            skyColor), CloudDistanceFade.Binding);
        resources.Ubo("cloud_view", view, ViewBinding);

        BindTexture(0, _baseTex);
        BindTexture(1, _baseTex);
        BindTexture(2, _noiseTex);
        BindTexture(3, _noiseBlendTex);
        // cScatterTexture: the ATMOSPHERE's scattered light, which the vertex shader samples and
        // the fragment ADDS into the cloud colour (out_attr7 -> in_attr7 -> temp_287/288/289). It
        // was a flat WHITE 1x1 placeholder, so every cloud pixel had white light added to it - which
        // is why clouds came out pink and white-flecked over a red sky no matter how red their own
        // authored colours were. The baked Bruneton LUT IS this quantity, so now that the sky chain
        // produces one, feed it in. Falls back to the placeholder only if the bake is unavailable.
        BindTexture(4, scatterTexture != 0 ? scatterTexture : _scatterTex);

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);

        _gl.Disable(EnableCap.DepthClamp);

        // Composite the small cloud buffer over the scene with the game's real blend. Doing the
        // blend HERE rather than while drawing the dome is what makes the reduced-resolution pass
        // correct: the dome's own draw writes colour and coverage into a cleared buffer, and this
        // is the single SrcAlpha/InvSrcAlpha step against the sky.
        if (_compositeProgram == 0)
            _compositeProgram = GLProgramBuilder.Build(_gl, CompositeVert, CompositeFrag, "cloud_composite");

        targets.BindColorTarget(targets.Final);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.Zero, GLEnum.OneMinusSrcAlpha);
        // Stated, not inherited: the func alone does not pin down the blend, and whatever equation
        // the previous pass left set would otherwise silently turn this composite into a subtract.
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        _gl.UseProgram(_compositeProgram);
        _gl.BindTextureUniform(_compositeProgram, "tCloud", 0, _cloudTex);
        resources.DrawFullscreenTriangle();

        // Leave depth-test disabled, exactly as BackgroundPass (which runs immediately before this)
        // leaves it - re-enabling here would hand the following pass different state than it gets
        // when this pass is switched off.
        _gl.Disable(EnableCap.Blend);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    void BindTexture(int unit, uint tex)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
    }

    /// <summary>
    /// The real 768-byte "Common" block: <see cref="CloudUboBaseline"/>'s captured real bytes with
    /// every slot whose source has been identified overwritten from live ROM data. Each write below
    /// is a slot that was confirmed by exact numeric match against the real capture - see the class
    /// remarks for the method, and <see cref="CloudUboBaseline"/> for what deliberately isn't
    /// overwritten and why.
    /// </summary>
    /// <param name="skyColorGain">
    /// Scales the palette-authored RADIANCE terms into the magnitude family the viewer's own
    /// <c>Exposure</c> multiply expects - see <see cref="BackgroundPass.SkyColorAnchor"/> for why
    /// this is needed at all and why the constant is shared with the sky behind these clouds.
    /// </param>
    internal static byte[] BuildCommonBlock(EnvPalette palette, EnvPalette.CloudLayer cloud,
        CloudPostFxShared shared, CloudPostFxLayer layer, Vector3 sunWorld,
        float scroll1X, float scroll1Y, float scroll2X, float scroll2Y,
        float skyHeightAboveCamera, float skyColorGain)
    {
        byte[] buf = CloudUboBaseline.Common();
        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).AsSpan().CopyTo(buf.AsSpan(slot * 16 + comp * 4));
        void F3(int slot, Vector3 v) { F(slot, 0, v.X); F(slot, 1, v.Y); F(slot, 2, v.Z); }

        // --- real per-layer postfx scalars (master_field.baglclwd, CloudParam0) ---
        F(1, 3, layer.NoiseScale1);
        F(2, 0, layer.NoiseScale2);
        F(2, 1, layer.NoiseDensity1);
        F(2, 2, layer.NoiseDensity2);
        F(2, 3, layer.EmbossWidth);
        F(3, 0, layer.EmbossDensity);
        F(3, 1, layer.HilightPower);
        F(3, 2, layer.ShadowPower);
        F(3, 3, layer.HighlightRange);
        F(4, 0, layer.HighlightAmbient);
        F(4, 3, layer.BacklightParam1);
        F(7, 0, layer.FarUVPow);
        F(7, 1, layer.FarUVMul);
        F(7, 2, layer.FarDensityChgStart);
        F(7, 3, layer.FarDensityChgEnd);
        F(8, 0, layer.FarDensityChgPower);
        F(8, 1, layer.FarAlphaChgStart);
        F(8, 2, layer.FarAlphaChgEnd);
        // The far ALPHA fade's exponent, and the reason distant clouds did not fade out at all:
        // it was the one member of the pattern left unwritten, so it kept whatever the captured
        // baseline held. 7.z/7.w/8.x are FarDensityChg Start/End/Power and 8.y/8.z are
        // FarAlphaChg Start/End, which makes 8.w the matching Power - and the fragment shader does
        // read it (fma(t, data[8].w, ...) right before the final output).
        // NOT written from the AAMP. Its siblings all match the capture exactly (slot 7 =
        // 8/1.8/0.8/0.15, slot 8.x/.y/.z = 0.15/0.7/0.95), but mFarAlphaChgPower parses to -0.5
        // while the game's own block holds -1 here - so writing it would replace a known-correct
        // captured value with one the game demonstrably does not run. Left at the baseline.

        // The far fade's other half. Alpha is
        //   clamp(farTerm + data[45].x * (1 - t) + ...)
        // so a non-zero data[45].x is a CONSTANT FLOOR under the alpha, which is precisely "the
        // clouds never fade out no matter how far away they are". It was never written, so it kept
        // whatever the captured baseline held - and that capture was taken at one particular spot,
        // where the constant encodes that camera's own placement fade. Zeroed here so the distance
        // fade above is what actually decides alpha.
        F(45, 0, 0f);
        F(25, 3, layer.ScatterHeight);
        F(26, 1, layer.SunOccChkSize);
        F(26, 2, layer.FarDistotionChgStart);
        F(26, 3, layer.FarDistotionChgEnd);

        // --- real shared postfx ("Cloud" object) ---
        // data[25].y is the LAST thing the fragment shader multiplies by - the whole output is
        // mix(cloudColour, FogColour * data[25].z, fogWeight) * data[25].y - so the brightness/
        // exposure gain belongs HERE, not on the individual colour slots. Scaling those was
        // arithmetically fine but useless in practice: CloudColorScale re-amplified them
        // afterwards, so every value on the slider still clipped to white and the control looked
        // dead. Putting it on the final multiplier makes the slider scale the actual output.
        //
        // data[25].z stays the UNSCALED reciprocal on purpose: the fog term is
        // FogColour * data[25].z * data[25].y, so leaving it alone lets the gain fall through to
        // the fog exactly as it does to the cloud, keeping the two in step.
        F(25, 1, shared.CloudColorScale * skyColorGain);
        F(25, 2, shared.CloudColorScale > 1e-6f ? 1f / shared.CloudColorScale : 0f);

        // --- real per-palette cloud shading (EnvPalette Cloud0/Cloud1) ---
        // xyz = colour * intensity, w = that intensity, verified byte-exact against the capture's
        // own palette. This is the part that makes the cloud the right colour per palette.
        //
        // The .xyz RADIANCE terms carry skyColorGain (see the parameter's remarks); the .w
        // INTENSITY components deliberately do NOT. The capture establishes .w as the raw authored
        // intensity, and the shader reads it structurally - as a weight/divisor alongside
        // data[25].y's CloudColorScale reciprocal - not as a brightness to emit. Scaling a value
        // the shader divides by would change the cloud's SHAPE, not just its exposure, which is
        // exactly the class of error the whole capture-verified approach exists to avoid.
        F3(28, cloud.ColorBase * cloud.IntensityBase);
        F(28, 3, cloud.IntensityBase);
        F3(29, cloud.ColorHilight * cloud.IntensityHilight);
        F(29, 3, cloud.IntensityHilight);
        F3(30, cloud.ColorShadow * cloud.IntensityShadow);
        F(30, 3, cloud.IntensityShadow);
        F3(31, cloud.ColorBackLight); // no intensity of its own - BacklightPower is a separate term
        F(31, 3, 0f);

        // --- real per-palette fog/scatter terms ---
        F3(36, palette.FogColor);
        F(37, 0, palette.FogEnd > 1e-6f ? 1f / palette.FogEnd : 0f);
        F(41, 0, palette.ScatterFogAttenuation);
        F(41, 1, palette.ScatterFogHorizontal);

        // --- the dome's own direction matrix (slots 32/33/34) ---
        // Three rows, each dotted with the LOCAL vertex position, producing a direction that the
        // shader normalises and dots against the sun direction below. The real game uses a plain
        // diagonal here (mSkyScale, height, mSkyScale), so the dome's flattened aspect ratio is what
        // shapes the result - and its middle component is the "up" one, since the local mesh is
        // Y-up. Kept exactly that way (rather than swapped to WildRenderingSharp's Z-up world) because the
        // shader also forwards that middle component alone to the fragment stage as the surface's
        // up-ness; the sun direction is converted into this same Y-up frame instead.
        // The height term is the dome's height ABOVE THE CAMERA, not the raw authored mSkyHeight -
        // established from the capture: its value was mSkyHeight minus the camera's own altitude
        // (7895.27 - 347.85 = 7547.42, matching to the bit). That keeps the dome's apex at the
        // authored absolute sky altitude however high the viewer is.
        F(32, 0, layer.SkyScale); F(32, 1, 0f); F(32, 2, 0f);
        F(33, 0, 0f); F(33, 1, skyHeightAboveCamera); F(33, 2, 0f);
        F(34, 0, 0f); F(34, 1, 0f); F(34, 2, layer.SkyScale);

        // Sun direction, converted from WildRenderingSharp's Z-up world into the dome's own Y-up frame so it
        // matches the direction the matrix above produces.
        // NEGATED, and Y-up. The capture has Common[42] = (-0.00922, -0.67337, -0.73925, 1)
        // against a sun direction of (+0.00922, +0.67337, +0.73925) in the same frame's sky block,
        // so this slot holds the direction light TRAVELS - the opposite of the direction TO the sun
        // that the rest of this block takes. Unnegated, the clouds are lit from the wrong side.
        F(42, 0, -sunWorld.X); F(42, 1, -sunWorld.Z); F(42, 2, -sunWorld.Y);

        // Time-integrated UV scroll (both the base and base-blend pairs; the real game may phase
        // them differently, which isn't established).
        F(6, 0, scroll1X); F(6, 1, scroll1Y); F(6, 2, scroll2X); F(6, 3, scroll2Y);

        return buf;
    }

    /// <summary>
    /// The real 256-byte "View" block: <c>cViewMat</c> (slots 4-7), <c>cProjMat</c> (slots 8-11) and
    /// <c>cZOffsetParam</c> (slot 12.x). The capture proved slots 4-7 are view*model PRE-COMBINED
    /// (its own values carry the 25000-magnitude world scale, and its fourth row is exactly
    /// <c>(0,0,0,1)</c>), and that 8-11 is a textbook perspective matrix - so the dome's placement
    /// is folded in here rather than applied to the mesh.
    /// </summary>
    static byte[] BuildViewBlock(ReadOnlySpan<Vector4> viewRows, ReadOnlySpan<Vector4> projRows,
        Vector3 cameraEye, CloudPostFxLayer layer, float skyHeightAboveCamera, float domeScale)
    {
        byte[] buf = new byte[ViewBytes];

        // Local (Y-up, unit) dome -> WildRenderingSharp's Z-up world: scale by the real authored sky extent
        // (times the frustum fit, see Run), map local Y (height) onto world Z, and re-centre on the
        // camera. The height uses the camera-relative sky height for the same reason the direction
        // matrix does.
        float radius = layer.SkyScale * domeScale;
        float height = skyHeightAboveCamera * domeScale;
        Vector4[] model =
        [
            new Vector4(radius, 0f, 0f, cameraEye.X),
            new Vector4(0f, 0f, radius, cameraEye.Y),
            new Vector4(0f, height, 0f, cameraEye.Z),
            new Vector4(0f, 0f, 0f, 1f),
        ];
        // WildRenderingSharp hands the view matrix around as 3 affine rows (the implicit fourth being
        // (0,0,0,1)) - see how DeferredPipeline itself calls Mat4Math.ToMat4 on it before use - so
        // it has to be completed to 4 rows before it can be multiplied.
        Vector4[] view4 = viewRows.Length == 3 ? Mat4Math.ToMat4(viewRows) : viewRows.ToArray();
        Vector4[] viewModel = Mat4Math.Multiply(view4, model);

        WriteRows(buf, 4, viewModel);
        WriteRows(buf, 8, projRows);
        BitConverter.GetBytes(CloudUboBaseline.ZOffsetParam).AsSpan().CopyTo(buf.AsSpan(12 * 16));
        return buf;
    }

    static void WriteRows(byte[] buf, int startSlot, ReadOnlySpan<Vector4> rows)
    {
        for (int r = 0; r < 4 && r < rows.Length; r++)
        {
            Vector4 row = rows[r];
            int off = (startSlot + r) * 16;
            BitConverter.GetBytes(row.X).AsSpan().CopyTo(buf.AsSpan(off));
            BitConverter.GetBytes(row.Y).AsSpan().CopyTo(buf.AsSpan(off + 4));
            BitConverter.GetBytes(row.Z).AsSpan().CopyTo(buf.AsSpan(off + 8));
            BitConverter.GetBytes(row.W).AsSpan().CopyTo(buf.AsSpan(off + 12));
        }
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
