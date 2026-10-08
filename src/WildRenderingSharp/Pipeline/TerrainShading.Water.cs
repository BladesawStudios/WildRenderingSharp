using System.Text.Json;
using System.Text.RegularExpressions;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// The game's terrain water - <c>Shader/terrain_water</c>'s program 98, exported by the preparer
/// (<c>ShaderLibrary.CompileTool.ExportTerrainWater</c>) - for a host's water.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the ground, the water's vertex stage is the game's too, because most of what the water
/// looks like is decided there: the water's height, flow and type are read from <c>tera_water</c>,
/// the six colours of its type looked up in <c>WaterAlb</c>, the ripples' texture coordinates
/// derived from world X/Z through the material's texture matrices. What the game's stage takes from
/// its own patch scheme is narrow - a unit grid position (<c>aPosition</c>) and the patch's
/// <c>TerrainNode</c> block, which places that grid in the world and on the patch's textures - so a
/// host supplies exactly that and nothing more, from a function the vertex stage calls first:
/// </para>
/// <code>
/// bool wrs_water_place();   // false: this vertex is not drawn
/// </code>
/// <para>
/// which sets <c>aPosition.xy</c> (0..1 across the patch), <c>wrs_water_layer</c> (the patch's
/// layer in the host's arrays) and <c>wrs_node[0..17]</c>, the patch's <c>TerrainNode</c>:
/// </para>
/// <list type="bullet">
/// <item>[0] grid to world X/Z: <c>(sizeX, sizeZ, minX, minZ)</c>.</item>
/// <item>[1].zw the distance morph, <c>(0, 1)</c> to turn it off; [2] and [3] are then unread.</item>
/// <item>[4].x the water texture's texel size (its centres are half a texel in).</item>
/// <item>[5] world to grid: <c>(1/size, -, -minX/size, -minZ/size)</c>.</item>
/// <item>[6] grid to the water texture: <c>uv = g * [6].x + [6].zw + [4].x / 2</c>.</item>
/// <item>[7] grid to the height texture, likewise, and [17].x its half texel (wave-type water only).</item>
/// </list>
/// <para>
/// The game's <c>tera_water</c> and <c>tera_height</c> become arrays read at
/// <c>wrs_water_layer</c>, bound by the host at <see cref="TeraWaterUnit"/> (RGBA, normalised: x the
/// height, yz the flow, w the type in its high byte with the no-water flag in the low byte's top
/// bit) and <see cref="TeraHeightUnit"/>. The host also fills <c>TerrainSystem</c> (11): [0].y and
/// [1].y the height scale and offset, [2] the camera, [4].y the depth bias of a flagged vertex,
/// [5] the flow's scale (<c>(1, 0, 0, 1)</c> for <c>2v - 1</c>), [11].z = 1 for the colours from
/// <c>WaterAlb</c>, [11].w the base the special types are counted from.
/// </para>
/// <para>
/// The renderer binds the rest: <c>Context</c> in the game's world, <c>Env</c>, the water
/// material, its textures, and what the fragment stage reads of the frame - the lit scene it
/// refracts, the material IDs and linear depth under it. Like actor water it draws after the
/// opaque scene is lit (see <see cref="SceneColorShapePass"/>), and is then marked for
/// <c>field_water</c> by drawing the same geometry again with <see cref="LinkWaterStampProgram"/>.
/// </para>
/// </remarks>
public sealed partial class TerrainShading
{
    /// <summary>Where the host binds its water and height arrays, and where <c>WaterAlb</c> goes - off the fragment stage's units.</summary>
    public const int TeraWaterUnit = 30, TeraHeightUnit = 31, WaterAlbUnit = 29;

    /// <summary>The G-buffer depth the stamp program compares against.</summary>
    internal const int StampDepthUnit = 28;

    /// <summary>The stamp program's block: <c>(pass id, near, far, -)</c>, <c>(1/width, 1/height, -, -)</c>.</summary>
    internal const uint StampBinding = Profiles.Totk.TotkBindings.TerrainWaterStamp;

    const string WaterProgramName = "terrain_water_prog98";

    uint _waterMaterial, _waterAlb, _waterNrm, _waterEmm;
    Task<WaterTextures?>? _waterLoad;

    /// <summary>Whether the preparer has exported the water program and its textures.</summary>
    public bool WaterAvailable =>
        File.Exists(Path.Combine(_shadersDir, "terrain_water_textures.json"))
        && File.Exists(Path.Combine(_shadersDir, WaterProgramName + "_extracted.frag"));

    /// <summary>Links the game's water program with <paramref name="hostSource"/> - see the remarks.</summary>
    public uint LinkWaterProgram(string hostSource)
    {
        string frag = GlslSanitizer.Clean(File.ReadAllText(Path.Combine(_shadersDir, WaterProgramName + "_extracted.frag")));
        // The game counts the water types it saw in a buffer of its own; here binding 0 is the bake table.
        frag = Regex.Replace(frag, @"fp_s0\.data\[[^\]]*\]\s*=\s*[^;]+;", "");
        return GLProgramBuilder.Build(_gl, WaterVertex(hostSource), frag, WaterProgramName);
    }

    /// <summary>The same vertex stage with a fragment stage marking each pixel the water won for its deferred pass.</summary>
    public uint LinkWaterStampProgram(string hostSource) =>
        GLProgramBuilder.Build(_gl, WaterVertex(hostSource), StampFragment, WaterProgramName + "_stamp");

    string WaterVertex(string hostSource)
    {
        string vert = GlslSanitizer.Clean(File.ReadAllText(Path.Combine(_shadersDir, WaterProgramName + "_extracted.vert")));

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
        return vert + """

            void main()
            {
                if (!wrs_water_place())
                {
                    gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
                    return;
                }
                wrs_game_water_main();
            }
            """;
    }

    const string StampFragment = """
        #version 450 core
        layout (binding = 28) uniform sampler2D wrs_gbuffer_depth;
        layout (binding = 3, std140) uniform _WrsStamp { vec4 wrs_stamp; vec4 wrs_viewport; };
        layout (location = 0) out vec4 fragColor;
        float viewDepth(float d)
        {
            float n = wrs_stamp.y, f = wrs_stamp.z;
            return (2.0 * n * f) / (f + n - (d * 2.0 - 1.0) * (f - n));
        }
        void main()
        {
            vec2 g = vec2(gl_FragCoord.x * wrs_viewport.x, 1.0 - gl_FragCoord.y * wrs_viewport.y);
            float kept = texture(wrs_gbuffer_depth, g).r;
            if (kept >= 1.0)
                discard;
            float zKept = viewDepth(kept), zThis = viewDepth(gl_FragCoord.z);
            if (abs(zKept - zThis) > max(0.02, zKept * 0.002))
                discard;
            fragColor = vec4(wrs_stamp.x, 0.0, 0.0, 1.0);
        }
        """;

    /// <summary>
    /// Binds the water's material (8) and textures, once they are loaded - the first call starts
    /// decoding them off the render thread and returns false until they are ready.
    /// </summary>
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
        if (_waterMaterial == 0)
        {
            string path = Path.Combine(_shadersDir, "terrain_water_material.bin");
            _waterMaterial = GLBuffer.CreatePaddedUniformBuffer(_gl, File.Exists(path) ? File.ReadAllBytes(path) : []);
        }
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, _bindings.Material, _waterMaterial);
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

    /// <summary>An array with a full mip chain, generated - the export carries mip 0, and ripples tiled across a lake shimmer without one.</summary>
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
        if (_waterMaterial != 0) _gl.DeleteBuffer(_waterMaterial);
    }

    /// <summary>The water's textures decoded for upload: <c>WaterAlb</c> as half floats, <c>WaterNrm</c> as RGBA8, <c>WaterEmm</c> as R8.</summary>
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
                Console.Error.WriteLine($"[terrain water] {ex.Message}");
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
