using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>One of the game's deferred passes, compiled with the mask that stands in for its depth-tested material ID.</summary>
public sealed record BotwPass(string Name, uint Program, MaterialBlock Material, float Id);

/// <summary>Loads the game's deferred passes and the small textures they read, and draws them as fullscreen triangles.</summary>
public sealed class BotwPasses(StageServices services) : IDisposable
{
    // The pass's vertex shader reads its compare ID from material slot 26, .z, and turns it into the depth the pass draws at.
    const int MaterialIdOffset = 26 * 16 + 8;

    readonly List<MaterialBlock> _blocks = [];
    uint _lightAnalyzed;
    (Vector3 Sky, Vector3 Ground) _analyzed;

    public uint White { get; } = Texture1x1(services.Gl, Vector4.One);
    public uint Black { get; } = Texture1x1(services.Gl, Vector4.Zero);
    public uint LightAnalyzed => _lightAnalyzed;

    /// <summary>The pass lighting material IDs <paramref name="idLow"/> to <paramref name="idHigh"/>, or null when the cache has no such pass.</summary>
    public BotwPass? Load(string name, float? idLow = null, float? idHigh = null)
    {
        string? file = Directory.EnumerateFiles(services.Directories.Decompiled, $"deferred_{name}_prog*_extracted.frag").Order().FirstOrDefault();
        string materialPath = Path.Combine(services.Directories.DeferredMaterials, $"{name}.gsys_material.bin");
        if (file is null || !File.Exists(materialPath))
            return null;

        var block = new MaterialBlock(services.Gl, File.ReadAllBytes(materialPath));
        _blocks.Add(block);
        float id = BitConverter.ToSingle(block.Authored[MaterialIdOffset..]);
        float low = idLow ?? id, high = idHigh ?? id;
        uint program = services.Programs.Load(Path.GetFileNameWithoutExtension(file),
            patchVertex: MoveLightAnalyzedSampler, patchFragment: source => MaskByMaterialId(source, low, high));
        return new BotwPass(name, program, block, id);
    }

    public void Draw(BotwPass pass)
    {
        var gl = services.Gl;
        services.Resources.BindMaterial(pass.Material);
        gl.UseProgram(pass.Program);
        gl.SetInt(pass.Program, "uIdTex", BotwSamplers.IdTexture);
        services.Resources.DrawFullscreenTriangle();
    }

    public void BindAt(int unit, uint handle, TextureTarget target = TextureTarget.Texture2D) => services.Gl.BindTextureAt(unit, handle, target);

    // Only the vertex shaders that read the ambient strip declare it.
    static string MoveLightAnalyzedSampler(string vertex) =>
        vertex.Replace("layout (binding = 0) uniform sampler2D cGSys23_LightAnalyzedData;",
            $"layout (binding = {BotwSamplers.LightAnalyzed}) uniform sampler2D cGSys23_LightAnalyzedData;");

    static string MaskByMaterialId(string fragment, float low, float high) =>
        fragment.ReplaceRequired("void main()\n{",
            "uniform sampler2D uIdTex;\nvoid main()\n{\n" +
            "    float materialId = texture(uIdTex, gl_FragCoord.xy / vec2(textureSize(uIdTex, 0))).x * 255.0;\n" +
            FormattableString.Invariant($"    if (materialId < {low - 0.5f:F1} || materialId > {high + 0.5f:F1}) discard;\n"));

    /// <summary>The vertex shader blends two samples of this strip, at 9/24 and 11/24, into the ambient colour it hands the fragment stage by screen height.</summary>
    public unsafe void EnsureLightAnalyzed(Vector3 sky, Vector3 ground)
    {
        if (_lightAnalyzed != 0 && _analyzed == (sky, ground))
            return;
        _analyzed = (sky, ground);
        var gl = services.Gl;
        if (_lightAnalyzed == 0)
            _lightAnalyzed = gl.GenTexture();

        const int Width = 24;
        var texels = new float[Width * 4];
        for (int i = 0; i < Width; i++)
        {
            Vector3 color = i <= 9 ? sky : ground;
            texels[i * 4] = color.X;
            texels[i * 4 + 1] = i < 2 ? 1f : color.Y;
            texels[i * 4 + 2] = color.Z;
            texels[i * 4 + 3] = 1f;
        }
        gl.BindTexture(TextureTarget.Texture2D, _lightAnalyzed);
        fixed (float* data = texels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, Width, 1, 0, PixelFormat.Rgba, PixelType.Float, data);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    static unsafe uint Texture1x1(GL gl, Vector4 color)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        float[] data = [color.X, color.Y, color.Z, color.W];
        fixed (float* p = data)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, p);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        return handle;
    }

    public void Dispose()
    {
        var gl = services.Gl;
        gl.DeleteTexture(White);
        gl.DeleteTexture(Black);
        if (_lightAnalyzed != 0)
            gl.DeleteTexture(_lightAnalyzed);
        foreach (var block in _blocks)
            block.Dispose();
    }
}
