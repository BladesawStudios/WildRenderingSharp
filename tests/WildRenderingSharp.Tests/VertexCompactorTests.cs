using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Loading;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Tests;

public class VertexCompactorTests
{
    static List<VertexLayoutEntry> Layout() =>
    [
        new() { Name = "aPosition", Location = 0, Offset = 0, Components = 3 },
        new() { Name = "aNormal", Location = 1, Offset = 12, Components = 3 },
        new() { Name = "aTexCoord", Location = 2, Offset = 24, Components = 2 },
    ];

    [Fact]
    public void PositionIsAlwaysUsedAndOnlyActiveLocationsAddToIt()
    {
        var used = VertexCompactor.UsedAttributes(Layout(), 0, false, [new HashSet<int> { 2 }]);

        Assert.Equal(["aPosition", "aTexCoord"], used.Order());
    }

    [Fact]
    public void ASkinnedShapeKeepsTheBlendAttributesUnlessTheConstantVertexReplacesThem()
    {
        Assert.Superset(VertexCompactor.BlendAttributes.ToHashSet(), VertexCompactor.UsedAttributes(Layout(), 1, false, []));
        Assert.Empty(VertexCompactor.UsedAttributes(Layout(), 2, true, []).Intersect(VertexCompactor.BlendAttributes));
    }

    [Fact]
    public void CompactRepacksTheKeptAttributesVertexByVertex()
    {
        float[] vertices = [1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 17, 18];
        var bytes = new byte[vertices.Length * sizeof(float)];
        Buffer.BlockCopy(vertices, 0, bytes, 0, bytes.Length);

        var (layout, stride, packed) = VertexCompactor.Compact(Layout(), 32, bytes, ["aPosition", "aTexCoord"]);

        var result = new float[packed.Length / sizeof(float)];
        Buffer.BlockCopy(packed, 0, result, 0, packed.Length);
        Assert.Equal(20, stride);
        Assert.Equal([12], layout.Skip(1).Select(e => e.Offset));
        Assert.Equal([1f, 2, 3, 7, 8, 11, 12, 13, 17, 18], result);
    }

    [Fact]
    public void CompactLeavesAFullLayoutUntouched()
    {
        var layout = Layout();
        byte[] bytes = new byte[64];

        var result = VertexCompactor.Compact(layout, 32, bytes, ["aPosition", "aNormal", "aTexCoord"]);

        Assert.Same(layout, result.Layout);
        Assert.Same(bytes, result.Bytes);
    }
}
