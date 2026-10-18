using WildRenderingSharp.Profiles.Totk.Shaders;
using Xunit;

namespace WildRenderingSharp.Tests;

public sealed class MergedShaderPatchTests
{
    const string Vertex = """
        #version 450 core
        layout (binding = 4, std140) uniform _ShpMtx
        {
            precise vec4 data[4096];
        } vp_c7;
        layout (binding = 8, std140) uniform _Mat
        {
            precise vec4 data[4096];
        } vp_c11;
        layout (location = 0) in vec4 aPosition;
        void main()
        {
            vec4 row = vp_c7.data[0];
            gl_Position = aPosition * vp_c11.data[3].x + row;
        }
        """;

    const string Fragment = """
        #version 450 core
        layout (binding = 8, std140) uniform _Mat
        {
            precise vec4 data[4096];
        } fp_c11;
        layout (binding = 15) uniform sampler2D cTexture0;
        layout (binding = 25) uniform sampler2D cTextureBake0;
        layout (binding = 23) uniform sampler2DArray cTextureArray0;
        layout (binding = 1) uniform sampler2D cTex_VolumeMask;
        layout (location = 0) out vec4 output_color;
        void main()
        {
            output_color = texture(cTexture0, vec2(0.0)) * fp_c11.data[39].w + texture(cTex_VolumeMask, vec2(0.0));
        }
        """;

    [Fact]
    public void TheVertexStageReadsItsMaterialAndPlacementFromTheDrawsRecord()
    {
        var (vertex, _) = MergedShaderPatch.Apply(Vertex, Fragment)!.Value;

        Assert.Contains("wrs_shp(int(0))", vertex);
        Assert.Contains("wrs_mat(int(3)).x", vertex);
        Assert.DoesNotContain("uniform _Mat", vertex);
        Assert.Contains("void wrs_inner_main()", vertex);
        Assert.EndsWith("wrs_inner_main();\n}", vertex.TrimEnd());
    }

    [Fact]
    public void TheBakedLightingIndexIsReadWiderThanTheGamesTwentyBitsSoAWholeMapsTableFits()
    {
        string baked = Vertex.Replace("vec4 row = vp_c7.data[0];", "vec4 row = vp_c7.data[0]; int t = floatBitsToInt(vp_c7.data[8].y) & 0xFFFFF;");

        var (vertex, _) = MergedShaderPatch.Apply(baked, Fragment)!.Value;

        Assert.Contains(".y) & 0x3FFFFFFF", vertex);
        Assert.DoesNotContain("0xFFFFF;", vertex);
    }

    [Fact]
    public void TheFragmentStageTakesItsShapeTexturesFromHandlesAndLeavesTheFramesAlone()
    {
        var (_, fragment) = MergedShaderPatch.Apply(Vertex, Fragment)!.Value;

        Assert.Contains("wrs_fmat(int(39)).w", fragment);
        Assert.Contains("cTexture0 = sampler2D(wrs_handles[wrs_v.y + 15]);", fragment);
        Assert.Contains("cTextureArray0 = sampler2DArray(wrs_handles[wrs_v.y + 23]);", fragment);
        Assert.Contains("cTextureBake0 = sampler2D(uvec2(uint(wrs_v.z), uint(wrs_v.w)));", fragment);
        Assert.Contains("layout (binding = 1) uniform sampler2D cTex_VolumeMask;", fragment);
        Assert.DoesNotContain("layout (binding = 15)", fragment);
    }

    [Fact]
    public void AProgramThatReadsPastTheMaterialSlotIsLeftToTheOrdinaryDraw()
    {
        string far = Fragment.Replace("fp_c11.data[39]", "fp_c11.data[64]");

        Assert.Null(MergedShaderPatch.Apply(Vertex, far));
    }

    [Fact]
    public void AMaterialReadThatIsNotAFixedIndexIsLeftToTheOrdinaryDraw()
    {
        string dynamic = Fragment.Replace("fp_c11.data[39]", "fp_c11.data[int(gl_FragCoord.x)]");

        Assert.Null(MergedShaderPatch.Apply(Vertex, dynamic));
    }
}
