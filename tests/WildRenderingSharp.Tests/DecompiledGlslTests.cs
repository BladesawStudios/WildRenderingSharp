using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Tests;

public sealed class DecompiledGlslTests
{
    const uint Orphan = 30;

    static string Clean(string source) => DecompiledGlsl.Clean(source, Orphan);

    [Fact]
    public void ExtensionsAndPragmasAreDroppedAndTheVersionStays()
    {
        string cleaned = Clean("#version 450 core\n#extension GL_ARB_gpu_shader_int64 : enable\n#pragma optionNV(fastmath off)\nvoid main() {}");

        Assert.StartsWith("#version 450 core\n", cleaned);
        Assert.DoesNotContain("#extension", cleaned);
        Assert.DoesNotContain("#pragma", cleaned);
    }

    [Fact]
    public void TheSafeMathDefinesFollowTheVersionLineOnce()
    {
        string cleaned = Clean("#version 450 core\nvoid main() {}");

        Assert.Equal(1, cleaned.Split("#define safe_log2").Length - 1);
        Assert.True(cleaned.IndexOf("#define safe_log2", StringComparison.Ordinal) > cleaned.IndexOf("#version", StringComparison.Ordinal));
    }

    [Fact]
    public void ALoneFragmentOutputArrayBecomesALocatedOutput()
    {
        string cleaned = Clean("layout (location = 0) out vec4 output_color[0];\nvoid main() { output_color[0].x = 1.0; }");

        Assert.Contains("layout (location = 0) out vec4 output_color0;", cleaned);
        Assert.Contains("output_color0.x = 1.0;", cleaned);
        Assert.DoesNotContain("output_color[0]", cleaned);
    }

    [Fact]
    public void ANegativeBindingMovesToTheOrphanBinding()
    {
        string cleaned = Clean("layout (binding = -3, std140) uniform _Driver { vec4 data[4]; } fp_c0;");

        Assert.Contains($"binding = {Orphan}, std140", cleaned);
    }

    [Fact]
    public void AFragmentStorageStoreIsRemovedButAComparisonIsKept()
    {
        string cleaned = Clean("void main() {\n    fp_s0.data[3] = 2u;\n    if (fp_s0.data[3] == 2u) discard;\n}");

        Assert.DoesNotContain("fp_s0.data[3] = 2u;", cleaned);
        Assert.Contains("fp_s0.data[3] == 2u", cleaned);
    }

    [Fact]
    public void TextureLodOnAnArrayShadowSamplerBecomesTextureGradWithZeroDerivatives()
    {
        string cleaned = Clean(
            "uniform sampler2DArrayShadow shadow;\nvoid main() { float v = textureLod(shadow, vec4(uv.x, f(a, b), 0.0, 0.5), 0.0); }");

        Assert.Contains("textureGrad(shadow, vec4(uv.x, f(a, b), 0.0, 0.5), vec2(0.0), vec2(0.0))", cleaned);
        Assert.DoesNotContain("textureLod", cleaned);
    }

    [Fact]
    public void TextureLodOnAnOrdinarySamplerIsLeftAlone()
    {
        string cleaned = Clean("uniform sampler2D albedo;\nvoid main() { vec4 v = textureLod(albedo, uv, 2.0); }");

        Assert.Contains("textureLod(albedo, uv, 2.0)", cleaned);
    }
}
