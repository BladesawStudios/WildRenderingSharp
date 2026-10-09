using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>The sun and the moon, drawn from the game's own sprites.</summary>
internal sealed class SkyBodyPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;
    readonly uint _sunTex;
    readonly uint[] _moonTex = new uint[8];

    public bool SunAvailable => _sunTex != 0;
    public bool MoonAvailable => _moonTex[0] != 0;

    static readonly string FragmentSource = GlslFiles.Load("Totk/Sky/SkyBody/Main.frag");

    public SkyBodyPass(GL gl, string? systemTexturesDirectory)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, FragmentSource, "sky_body");
        if (string.IsNullOrEmpty(systemTexturesDirectory) || !Directory.Exists(systemTexturesDirectory))
        {
            Log.Warning("[SkyBodyPass] no system-texture directory - sun/moon unavailable until extraction runs.");
            return;
        }

        _sunTex = LoadSingle(systemTexturesDirectory, "SunDisc", channels: 1);
        for (int i = 0; i < 8; i++)
            _moonTex[i] = LoadSingle(systemTexturesDirectory, $"Moon{i + 1}", channels: 2);

        Log.Info($"[SkyBodyPass] sun={(_sunTex != 0 ? "loaded" : "missing")}, " +
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
        Vector3 SunDir, Vector3 MoonDir,
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

        // Column-major for GL. A local function would close over the span, which C# forbids, so the nine writes are spelled out.
        Vector4 r0 = viewInv3Rows[0], r1 = viewInv3Rows[1], r2 = viewInv3Rows[2];
        Span<float> m = stackalloc float[9]
        {
            r0.X, r1.X, r2.X,
            r0.Y, r1.Y, r2.Y,
            r0.Z, r1.Z, r2.Z,
        };
        int loc = _gl.UniformLocation(_program, "uViewInv");
        if (loc >= 0) _gl.UniformMatrix3(loc, 1, false, m);

        _gl.SetVec2(_program, "uTanHalf", new Vector2(aspect * tanHalfFovY, tanHalfFovY));
        _gl.SetVec3(_program, "uSunDir", Normalise(p.SunDir));
        _gl.SetVec3(_program, "uMoonDir", Normalise(p.MoonDir));
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
        if (_program != 0) _gl.ReleaseProgram(_program);
        if (_sunTex != 0) _gl.DeleteTexture(_sunTex);
        foreach (uint t in _moonTex)
            if (t != 0) _gl.DeleteTexture(t);
    }
}
