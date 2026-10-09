using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Tests;

public sealed class SizedLruCacheTests
{
    static SizedLruCache<byte[]> Cache(long budget) => new(budget, bytes => bytes.Length);

    [Fact]
    public void TheLeastRecentlyUsedEntriesGoFirstOnceTheBudgetIsPassed()
    {
        var cache = Cache(10);
        cache.Add("a", new byte[4]);
        cache.Add("b", new byte[4]);
        cache.TryGet("a", out _);
        cache.Add("c", new byte[4]);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(8, cache.TotalSize);
    }

    [Fact]
    public void AnEntryLargerThanTheBudgetIsKeptAloneUntilTheNextOne()
    {
        var cache = Cache(10);
        cache.Add("big", new byte[50]);
        Assert.True(cache.TryGet("big", out _));

        cache.Add("small", new byte[2]);
        Assert.False(cache.TryGet("big", out _));
        Assert.Equal(2, cache.TotalSize);
    }

    [Fact]
    public void AddingAKeyThatIsHeldReturnsTheHeldValue()
    {
        var cache = Cache(100);
        var first = cache.Add("a", new byte[3]);

        Assert.Same(first, cache.Add("A", new byte[9]));
        Assert.Equal(3, cache.TotalSize);
    }
}
