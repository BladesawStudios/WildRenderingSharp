using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Assets.Textures;
using System.Text.Json;
using System.Text.RegularExpressions;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>
/// The game's terrain water - <c>Shader/terrain_water</c>'s program 98, exported by the preparer
/// (<c>ShaderLibrary.CompileTool.ExportTerrainWater</c>) - for a host's water.
/// </summary>
public sealed partial class TerrainShading
{
    public const int TeraWaterUnit = 30, TeraHeightUnit = 31, WaterAlbUnit = 29;

    internal const int StampDepthUnit = 28;

    internal const uint StampBinding = Profiles.Totk.TotkBindings.TerrainWaterStamp;

    const string WaterProgramName = "terrain_water_prog98";

    MaterialBlock? _waterMaterial;
    uint _waterAlb, _waterNrm, _waterEmm;
    Task<WaterTextures?>? _waterLoad;

    public bool WaterAvailable =>
        File.Exists(Path.Combine(_shadersDir, "terrain_water_textures.json"))
        && File.Exists(Path.Combine(_shadersDir, WaterProgramName + "_extracted.frag"));

    public uint LinkWaterProgram(string hostSource)
    {
        string frag = TotkGlsl.Clean(File.ReadAllText(Path.Combine(_shadersDir, WaterProgramName + "_extracted.frag")));
        // The game counts the water types it saw in a buffer of its own; here binding 0 is the bake table.
        frag = Regex.Replace(frag, @"fp_s0\.data\[[^\]]*\]\s*=\s*[^;]+;", "");
        return GLProgramBuilder.Build(_gl, WaterVertex(hostSource), frag, WaterProgramName);
    }

    public uint LinkWaterStampProgram(string hostSource) =>
        GLProgramBuilder.Build(_gl, WaterVertex(hostSource), StampFragment, WaterProgramName + "_stamp");

    string WaterVertex(string hostSource)
    {
        string vert = TotkGlsl.Clean(File.ReadAllText(Path.Combine(_shadersDir, WaterProgramName + "_extracted.vert")));

        vert = Regex.Replace(vert, @"layout \(location = 0\) in vec4 aPosition;",
            "vec4 aPosition = vec4(0.0, 0.0, 0.0, 1.0);\nvec4 wrs_node[18];\nfloat wrs_water_layer;");
        vert = Regex.Replace(vert, @"layout \(binding = \d+, std140\) uniform _TerrainNode\s*\{[^}]*\}\s*vp_c15;", "");
        vert = vert.Replace("vp_c15.data[", "wrs_node[");

        vert = Regex.Replace(vert, @"layout \(binding = \d+\) uniform sampler2D cTeraTexNodeWater;",
            $"layout (binding = {TeraWaterUnit}) uniform sampler2DArray cTeraTexNodeWater;");
        vert = Regex.Replace(vert, @"layout \(binding = \d+\) uniform sampler2D cTeraTexNodeHeight;",
            $"layout (binding = {TeraHeightUnit}) uniform sampler2DArray cTeraTexNodeHeight;");
        // WaterAlb shares unit 2 with the fragment stage's material IDs, of another type.
        vert = Regex.Replace(vert, @"layout \(binding = \d+\) uniform sampler2DArray cTexture0;",
            $"layout (binding = {WaterAlbUnit}) uniform sampler2DArray cTexture0;");
        vert = Regex.Replace(vert, @"texture\((cTeraTexNode\w+), vec2\(([^()]*)\)\)", "texture($1, vec3($2, wrs_water_layer))");

        string host = Regex.Replace(hostSource, @"^\s*#version[^\n]*\n", "", RegexOptions.Multiline);
        int main = vert.IndexOf("void main()", StringComparison.Ordinal);
        if (main < 0)
            throw new InvalidOperationException($"{WaterProgramName}: no main");
        vert = vert[..main] + "// ---- host ----\n" + host + "\n// ---- game ----\n" + vert[main..].Replace("void main()", "void wrs_game_water_main()");
        return vert + GlslFiles.Load("Totk/Terrain/TerrainShading/WaterVertexEpilogue.glsl");
    }

    static readonly string StampFragment = GlslFiles.Load("Totk/Terrain/TerrainShading/Stamp.frag");

    // Binds the water's material (8) and textures, once they are loaded - the first call starts decoding them off the render
    // thread and returns false until they are ready.
    internal bool BindWater()
    {
        if (_waterNrm == 0)
        {
            _waterLoad ??= Task.Run(() => WaterTextures.Load(_shadersDir));
            if (!_waterLoad.IsCompleted)
                return false;
            if (_waterLoad.Result is not { } loaded)
                return false;
            Upload(loaded);
        }
        _waterMaterial ??= MaterialBlock.FromFile(_gl, Path.Combine(_shadersDir, "terrain_water_material.bin"));
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, _bindings.Material, _waterMaterial.Handle);
        BindArray(WaterAlbUnit, _waterAlb);
        // _s0, _n0, _t0, _a1: the normals; _e0: the emission.
        BindArray(14, _waterNrm);
        BindArray(15, _waterNrm);
        BindArray(17, _waterNrm);
        BindArray(18, _waterNrm);
        BindArray(16, _waterEmm);
        return true;
    }

    void BindArray(int unit, uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2DArray, texture);
    }

    unsafe void Upload(WaterTextures t)
    {
        _waterAlb = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, _waterAlb);
        _gl.TexStorage3D(TextureTarget.Texture2DArray, 1, SizedInternalFormat.Rgba16f, (uint)t.AlbWidth, (uint)t.AlbHeight, (uint)t.AlbLayers);
        fixed (byte* p = t.Alb)
            _gl.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, (uint)t.AlbWidth, (uint)t.AlbHeight, (uint)t.AlbLayers,
                PixelFormat.Rgba, PixelType.HalfFloat, p);
        Sampling(mips: false, repeat: false);

        _waterNrm = Chain(t.Nrm, t.NrmWidth, t.NrmHeight, t.NrmLayers, SizedInternalFormat.Rgba8, PixelFormat.Rgba);
        _waterEmm = Chain(t.Emm, t.EmmWidth, t.EmmHeight, t.EmmLayers, SizedInternalFormat.R8, PixelFormat.Red);
        GLDiagnostics.Check(_gl, "terrain water textures");
    }

    // An array with a full mip chain, generated - the export carries mip 0, and ripples tiled across a lake shimmer without
    // one.
    unsafe uint Chain(byte[] data, int width, int height, int layers, SizedInternalFormat format, PixelFormat pixels)
    {
        uint texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, texture);
        uint levels = (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
        _gl.TexStorage3D(TextureTarget.Texture2DArray, levels, format, (uint)width, (uint)height, (uint)layers);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = data)
            _gl.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, (uint)width, (uint)height, (uint)layers, pixels, PixelType.UnsignedByte, p);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        _gl.GenerateMipmap(TextureTarget.Texture2DArray);
        Sampling(mips: true, repeat: true);
        return texture;
    }

    void Sampling(bool mips, bool repeat)
    {
        var t = TextureTarget.Texture2DArray;
        _gl.TexParameter(t, TextureParameterName.TextureMinFilter, (int)(mips ? GLEnum.LinearMipmapLinear : GLEnum.Linear));
        _gl.TexParameter(t, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        var wrap = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(t, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(t, TextureParameterName.TextureWrapT, wrap);
        if (mips)
            _gl.TexParameter(t, (TextureParameterName)GLEnum.TextureMaxAnisotropy, 8f);
    }

    void DisposeWater()
    {
        foreach (uint t in new[] { _waterAlb, _waterNrm, _waterEmm })
            if (t != 0) _gl.DeleteTexture(t);
        _waterMaterial?.Dispose();
    }

    // The water's textures decoded for upload: WaterAlb as half floats, WaterNrm as RGBA8, WaterEmm as R8.
    sealed class WaterTextures
    {
        public byte[] Alb = [], Nrm = [], Emm = [];
        public int AlbWidth, AlbHeight, AlbLayers, NrmWidth, NrmHeight, NrmLayers, EmmWidth, EmmHeight, EmmLayers;

        sealed record Entry(string name, string file, string format, int width, int height, int layers);

        public static WaterTextures? Load(string dir)
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Path.Combine(dir, "terrain_water_textures.json"))) ?? [];
                var result = new WaterTextures();
                foreach (var e in entries)
                {
                    byte[] raw = File.ReadAllBytes(Path.Combine(dir, e.file));
                    switch (e.name)
                    {
                        case "WaterAlb":
                            (result.Alb, result.AlbWidth, result.AlbHeight, result.AlbLayers) = (raw, e.width, e.height, e.layers);
                            break;
                        case "WaterNrm":
                            result.Nrm = DecodeSlices(raw, e, 4);
                            (result.NrmWidth, result.NrmHeight, result.NrmLayers) = (e.width, e.height, e.layers);
                            break;
                        case "WaterEmm":
                            result.Emm = DecodeSlices(raw, e, 1);
                            (result.EmmWidth, result.EmmHeight, result.EmmLayers) = (e.width, e.height, e.layers);
                            break;
                    }
                }
                if (result.Alb.Length == 0 || result.Nrm.Length == 0 || result.Emm.Length == 0)
                    throw new InvalidDataException("terrain water textures incomplete");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"[terrain water] {ex.Message}");
                return null;
            }
        }

        static byte[] DecodeSlices(byte[] raw, Entry e, int channels)
        {
            var info = CompressedTextureFormat.Resolve(e.format) ?? throw new NotSupportedException(e.format);
            int slice = CompressedTextureFormat.ComputeDataLength(info, e.width, e.height);
            int texels = e.width * e.height;
            var output = new byte[texels * channels * e.layers];
            Parallel.For(0, e.layers, layer =>
            {
                byte[] block = raw.AsSpan(layer * slice, slice).ToArray();
                byte[] decoded = info.AstcFootprint is { } footprint
                    ? CompressedTextureFormat.DecodeAstc(block, e.width, e.height, footprint, srgb: false)
                    : DecodeBc4(block, e.width, e.height);
                decoded.CopyTo(output, layer * texels * channels);
            });
            return output;
        }

        static byte[] DecodeBc4(byte[] data, int width, int height)
        {
            var output = new byte[width * height];
            Span<byte> palette = stackalloc byte[8];
            int blocksX = (width + 3) / 4, blocksY = (height + 3) / 4;
            for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                int at = (by * blocksX + bx) * 8;
                byte r0 = data[at], r1 = data[at + 1];
                palette[0] = r0;
                palette[1] = r1;
                if (r0 > r1)
                    for (int i = 1; i < 7; i++) palette[i + 1] = (byte)(((7 - i) * r0 + i * r1) / 7);
                else
                {
                    for (int i = 1; i < 5; i++) palette[i + 1] = (byte)(((5 - i) * r0 + i * r1) / 5);
                    palette[6] = 0;
                    palette[7] = 255;
                }
                ulong bits = 0;
                for (int i = 0; i < 6; i++) bits |= (ulong)data[at + 2 + i] << (8 * i);
                for (int i = 0; i < 16; i++)
                {
                    int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                    if (x < width && y < height)
                        output[y * width + x] = palette[(int)((bits >> (3 * i)) & 7)];
                }
            }
            return output;
        }
    }
}
