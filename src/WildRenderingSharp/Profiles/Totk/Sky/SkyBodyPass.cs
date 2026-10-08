using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>The sun and the moon, drawn from the game's own sprites.</summary>
/// <remarks>
/// <para>
/// Both are romfs assets: <c>TexToGo/Etc_Sun_A_Alb.txtg</c> is a 64x64 BC4 disc mask (single channel; the colour comes from the palette's sun colour, so one texture covers every
/// time of day) and <c>Etc_Moon_A_Alb.1</c> to <c>.8</c> are 256x256 BC5 sprites, the moon's eight phases. <c>SystemTextures.ExtractSkyBodyTextures</c> pulls them into the shared
/// <c>_system_textures</c> cache.
/// </para>
/// <para>
/// Drawn as a fullscreen pass, not billboard geometry. Each pixel reconstructs its view ray (the same tanHalfFov and inverse-view basis as every other sky pass) and is placed into
/// the body's tangent frame around its direction, so there is no quad to orient, no projection to get right and no clipping of a body near the screen edge, and it behaves
/// identically at any FOV or aspect.
/// </para>
/// <para>
/// The moon is a sprite, not <c>Model/Obj_Moon_A.Obj_Moon_A_01.bfres.mc</c>: the model is a lit, shaded object needing a place in the world at true sky distance, while the sprite
/// is what produces the moon's look, phases included. The model suits a close-up; the sprite suits a sky body.
/// </para>
/// <para>Runs after the sky and before the cloud dome, so cloud draws over both.</para>
/// </remarks>
public sealed class SkyBodyPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;
    readonly uint _sunTex;
    readonly uint[] _moonTex = new uint[8];

    public bool SunAvailable => _sunTex != 0;
    public bool MoonAvailable => _moonTex[0] != 0;

    const string FragmentSource = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tSun;      // BC4 disc mask, R only
        uniform sampler2D tMoon;     // BC5 sprite: R = albedo, G = coverage
        uniform mat3 uViewInv;       // camera-to-world rotation, rows already Y-up swapped
        uniform vec2 uTanHalf;
        uniform vec3 uSunDir;        // world, Y-up, normalised
        uniform vec3 uMoonDir;
        uniform vec3 uSunColor;
        uniform vec3 uMoonColor;
        uniform float uSunRadius;    // angular radius, radians
        uniform float uMoonRadius;
        uniform int uDrawSun, uDrawMoon;
        out vec4 oCol;

        // Places `dir` into a tangent frame around `centre` and returns sprite UVs in [0,1],
        // plus whether the pixel is inside the sprite at all.
        bool bodyUv(vec3 dir, vec3 centre, float radius, out vec2 uv)
        {
            // Reject behind the body or beyond its disc; the dot test keeps a body from also appearing at its antipode, which a tangent-plane projection would allow.
            if (dot(dir, centre) <= 0.0) return false;
            vec3 up = abs(centre.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
            vec3 right = normalize(cross(up, centre));
            vec3 realUp = cross(centre, right);
            // Divide by the dot so the sprite stays square toward the screen edge: a gnomonic projection, as a real billboard does.
            float d = dot(dir, centre);
            vec2 t = vec2(dot(dir, right), dot(dir, realUp)) / (d * radius);
            uv = t * 0.5 + 0.5;
            return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
        }

        void main()
        {
            vec2 ndc = vUV * 2.0 - 1.0;
            vec3 viewRay = vec3(ndc.x * uTanHalf.x, ndc.y * uTanHalf.y, -1.0);
            vec3 dir = normalize(uViewInv * viewRay);

            vec3 acc = vec3(0.0);
            float cover = 0.0;
            vec2 uv;

            // Moon first so the sun composites over it on the rare overlap.
            if (uDrawMoon == 1 && bodyUv(dir, uMoonDir, uMoonRadius, uv))
            {
                vec2 m = texture(tMoon, vec2(uv.x, 1.0 - uv.y)).rg;
                // R is the lit albedo, G the coverage or phase mask; multiplying keeps the unlit limb transparent instead of a dark disc.
                float a = m.r * m.g;
                acc += uMoonColor * a;
                cover = max(cover, a);
            }

            if (uDrawSun == 1 && bodyUv(dir, uSunDir, uSunRadius, uv))
            {
                float s = texture(tSun, vec2(uv.x, 1.0 - uv.y)).r;
                acc += uSunColor * s;
                cover = max(cover, s);
            }

            oCol = vec4(acc, cover);
        }
        """;

    public SkyBodyPass(GL gl, string? systemTexturesDirectory)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, FragmentSource, "sky_body");
        if (string.IsNullOrEmpty(systemTexturesDirectory) || !Directory.Exists(systemTexturesDirectory))
        {
            Console.WriteLine("[SkyBodyPass] no system-texture directory - sun/moon unavailable until extraction runs.");
            return;
        }

        _sunTex = LoadSingle(systemTexturesDirectory, "SunDisc", channels: 1);
        for (int i = 0; i < 8; i++)
            _moonTex[i] = LoadSingle(systemTexturesDirectory, $"Moon{i + 1}", channels: 2);

        Console.WriteLine($"[SkyBodyPass] sun={(_sunTex != 0 ? "loaded" : "missing")}, " +
            $"moon phases loaded: {_moonTex.Count(t => t != 0)}/8");
    }

    unsafe uint LoadSingle(string dir, string name, int channels)
    {
        string dataPath = Path.Combine(dir, name + (channels == 1 ? ".r8" : ".rg8"));
        string dimsPath = Path.Combine(dir, name + ".dims.txt");
        if (!File.Exists(dataPath) || !File.Exists(dimsPath))
            return 0;

        var parts = File.ReadAllText(dimsPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h))
            return 0;

        byte[] data = File.ReadAllBytes(dataPath);
        if (data.Length < w * h * channels)
            return 0;

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        // Row alignment matters: these are 1- and 2-byte-per-texel uploads, and the default 4-byte unpack alignment shears any width not a multiple of 4.
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        var (ifmt, fmt) = channels == 1
            ? (InternalFormat.R8, PixelFormat.Red)
            : (InternalFormat.RG8, PixelFormat.RG);
        fixed (byte* p = data)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, ifmt, (uint)w, (uint)h, 0, fmt, PixelType.UnsignedByte, p);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        // Clamped, not wrapped: the sprite is a disc on transparent ground and a wrapped edge would tile ghost copies across the sky.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return tex;
    }

    public readonly record struct Params(
        Vector3 SunDirZUp, Vector3 MoonDirZUp,
        Vector3 SunColor, Vector3 MoonColor,
        float SunAngularRadius, float MoonAngularRadius,
        int MoonPhase, bool DrawSun, bool DrawMoon);

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture target,
        ReadOnlySpan<Vector4> viewInv3Rows, float aspect, float tanHalfFovY, Params p)
    {
        bool sun = p.DrawSun && _sunTex != 0;
        int phase = NearestAvailablePhase(Math.Clamp(p.MoonPhase, 1, 8) - 1);
        bool moon = p.DrawMoon && phase >= 0 && _moonTex[phase] != 0;
        if (!sun && !moon)
            return;

        targets.BindColorTarget(target);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        // Straight alpha over the sky, not additive: an additive sun over a bright sky clips to white and loses the disc, and the moon must be able to be darker than the sky behind it.
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.Zero, GLEnum.One);
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);

        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tSun", 0, sun ? _sunTex : 0);
        _gl.BindTextureUniform(_program, "tMoon", 1, moon ? _moonTex[phase] : 0);

        // Column-major for GL, with the Z-up to Y-up row swap (world Z feeds y, world Y feeds z), as BuildContext does for the real sky program. A local function would close over the span, which C# forbids, so the nine writes are spelled out.
        Vector4 r0 = viewInv3Rows[0], r1 = viewInv3Rows[2], r2 = viewInv3Rows[1];
        Span<float> m = stackalloc float[9]
        {
            r0.X, r1.X, r2.X,
            r0.Y, r1.Y, r2.Y,
            r0.Z, r1.Z, r2.Z,
        };
        int loc = _gl.GetUniformLocation(_program, "uViewInv");
        if (loc >= 0) _gl.UniformMatrix3(loc, 1, false, m);

        _gl.SetVec2(_program, "uTanHalf", new Vector2(aspect * tanHalfFovY, tanHalfFovY));
        _gl.SetVec3(_program, "uSunDir", Normalise(SkyAxes.ToYUp(p.SunDirZUp)));
        _gl.SetVec3(_program, "uMoonDir", Normalise(SkyAxes.ToYUp(p.MoonDirZUp)));
        _gl.SetVec3(_program, "uSunColor", p.SunColor);
        _gl.SetVec3(_program, "uMoonColor", p.MoonColor);
        _gl.SetFloat(_program, "uSunRadius", MathF.Max(1e-4f, p.SunAngularRadius));
        _gl.SetFloat(_program, "uMoonRadius", MathF.Max(1e-4f, p.MoonAngularRadius));
        _gl.SetInt(_program, "uDrawSun", sun ? 1 : 0);
        _gl.SetInt(_program, "uDrawMoon", moon ? 1 : 0);

        resources.DrawFullscreenTriangle();
        _gl.Disable(EnableCap.Blend);
    }

    // The requested phase, or the closest that loaded: Etc_Moon_A_Alb.5 is authored in a TXTG format the extractor does not
    // decode (0x107), and the nearest neighbour is a better failure than no moon.
    int NearestAvailablePhase(int want)
    {
        if (_moonTex[want] != 0)
            return want;
        for (int d = 1; d < 8; d++)
        {
            int lo = want - d, hi = want + d;
            if (lo >= 0 && _moonTex[lo] != 0) return lo;
            if (hi < 8 && _moonTex[hi] != 0) return hi;
        }
        return -1;
    }

    static Vector3 Normalise(Vector3 v) =>
        v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : new Vector3(0f, 1f, 0f);

    public void Dispose()
    {
        if (_program != 0) _gl.DeleteProgram(_program);
        if (_sunTex != 0) _gl.DeleteTexture(_sunTex);
        foreach (uint t in _moonTex)
            if (t != 0) _gl.DeleteTexture(t);
    }
}
