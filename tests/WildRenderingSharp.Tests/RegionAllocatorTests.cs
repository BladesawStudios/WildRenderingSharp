using WildRenderingSharp.Pipeline.Merged;
using Xunit;

namespace WildRenderingSharp.Tests;

public sealed class RegionAllocatorTests
{
    [Fact]
    public void RunsAreHandedOutFromTheStartAndNeverOverlap()
    {
        var allocator = new RegionAllocator(100);

        Assert.Equal(0, allocator.Allocate(30));
        Assert.Equal(30, allocator.Allocate(50));
        Assert.Equal(80, allocator.Allocate(20));
        Assert.Equal(-1, allocator.Allocate(1));
        Assert.Equal(100, allocator.Used);
    }

    [Fact]
    public void AFreedRunIsReusedAndRunsThatTouchAreJoined()
    {
        var allocator = new RegionAllocator(100);
        int a = allocator.Allocate(40), b = allocator.Allocate(40), c = allocator.Allocate(20);

        allocator.Free(a, 40);
        allocator.Free(b, 40);

        Assert.Equal(c, 80);
        Assert.Equal(0, allocator.Allocate(80));
    }

    [Fact]
    public void FreeingBetweenTwoFreeRunsJoinsAllThree()
    {
        var allocator = new RegionAllocator(90);
        int a = allocator.Allocate(30), b = allocator.Allocate(30), c = allocator.Allocate(30);

        allocator.Free(a, 30);
        allocator.Free(c, 30);
        allocator.Free(b, 30);

        Assert.Equal(0, allocator.Used);
        Assert.Equal(0, allocator.Allocate(90));
    }

    [Fact]
    public void GrowingAddsFreeElementsAfterTheEnd()
    {
        var allocator = new RegionAllocator(10);
        allocator.Allocate(10);

        allocator.Grow(25);

        Assert.Equal(10, allocator.Allocate(15));
        Assert.Equal(-1, allocator.Allocate(1));
    }
}
