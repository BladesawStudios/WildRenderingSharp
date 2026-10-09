using WildRenderingSharp.Hosting.Preparers;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Tests;

public class PreparerProtocolTests
{
    [Fact]
    public void ASplitLineYieldsItsTabSeparatedNameAndValue()
    {
        Assert.True(PreparerProtocol.TrySplit("WRS_DONE Link\tLink_Model", PreparerProtocol.DonePrefix, out string name, out string value));
        Assert.Equal("Link", name);
        Assert.Equal("Link_Model", value);
    }

    [Theory]
    [InlineData("WRS_DONE no tab here")]
    [InlineData("WRS_FAIL Link\tboom")]
    [InlineData("plain progress")]
    public void ALineWithoutThePrefixOrTheTabIsNotSplit(string line)
    {
        Assert.False(PreparerProtocol.TrySplit(line, PreparerProtocol.DonePrefix, out _, out _));
    }

    [Fact]
    public void ABatchCommandCarriesTheSharedOptionsAfterItsOwn()
    {
        var request = new PrepareBatchRequest("rom", ["Link"], new CacheLayout("cache"), ["modA", "modB"], ImportAnimations: false, Force: true);

        var args = PreparerProtocol.PrepareBatch(request, "list.txt", 3);

        Assert.Equal(["prepare-batch", "--romfs", "rom", "--cache", "cache", "--list", "list.txt", "--jobs", "3",
            "--mod", "modA", "--mod", "modB", "--no-anims", "--force"], args);
    }
}
