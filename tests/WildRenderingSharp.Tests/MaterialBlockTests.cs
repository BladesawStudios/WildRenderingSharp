using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Materials;

namespace WildRenderingSharp.Tests;

public class MaterialBlockTests
{
    [Fact]
    public void AShortBlockIsZeroPaddedToTheBufferSize()
    {
        var padded = MaterialBlock.Padded([1, 2, 3]);

        Assert.Equal(MaterialBlock.BufferSize, padded.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 0 }, padded[..4]);
    }

    [Fact]
    public void AnOversizeBlockIsTruncated()
    {
        var padded = MaterialBlock.Padded(new byte[MaterialBlock.BufferSize + 16]);

        Assert.Equal(MaterialBlock.BufferSize, padded.Length);
    }

    [Fact]
    public void AnEmptyBlockIsAllZero()
    {
        Assert.All(MaterialBlock.Padded([]), b => Assert.Equal(0, b));
    }
}
