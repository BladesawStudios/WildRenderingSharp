using WildRenderingSharp.Imaging;
using WildRenderingSharp.RenderRegression;

namespace WildRenderingSharp.Tests;

public class RenderRegressionToolsTests
{
    static Image Gradient(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = ((byte)i, (byte)(i * 3), (byte)(i * 7), 255);
        return new Image(width, height, rgba);
    }

    [Fact]
    public void APngTheRendererWritesReadsBackUnchanged()
    {
        var image = Gradient(37, 19);
        using var stream = new MemoryStream();
        PngWriter.WriteRgba(stream, image.Width, image.Height, image.Rgba);

        var read = PngReader.Decode(stream.ToArray());

        Assert.Equal((image.Width, image.Height), (read.Width, read.Height));
        Assert.Equal(image.Rgba, read.Rgba);
    }

    [Fact]
    public void IdenticalImagesDifferByNothing()
    {
        var stats = ImageDiff.Compare(Gradient(8, 8), Gradient(8, 8));

        Assert.Equal(new DiffStats(0, 0), stats);
        Assert.True(stats.Within(DiffStats.DefaultTolerance));
    }

    [Fact]
    public void OneVisiblyChangedPixelIsCountedAndMeasured()
    {
        var baseline = Gradient(10, 10);
        var changed = baseline with { Rgba = (byte[])baseline.Rgba.Clone() };
        changed.Rgba[0] = (byte)(baseline.Rgba[0] + 100);

        var stats = ImageDiff.Compare(baseline, changed);

        Assert.Equal(1 / 100.0, stats.FractionOver16);
        Assert.Equal(100 / 300.0, stats.MeanAbs, precision: 6);
        Assert.False(stats.Within(DiffStats.DefaultTolerance));
    }

    [Fact]
    public void ImagesOfDifferentSizesNeverMatch()
    {
        Assert.False(ImageDiff.Compare(Gradient(8, 8), Gradient(8, 9)).Within(new DiffStats(1000, 1)));
    }

    [Fact]
    public void TheVisualizedDifferenceScalesEachChannelAndKeepsAlphaOpaque()
    {
        var a = new Image(1, 1, [10, 20, 30, 255]);
        var b = new Image(1, 1, [11, 20, 25, 0]);

        Assert.Equal([8, 0, 40, 255], ImageDiff.Visualize(a, b).Rgba);
    }
}
