using SkiaSharp;
using System.Buffers.Binary;
using System.IO.Compression;

namespace WildRenderingSharp.Imaging;

/// <summary>A decoded 8-bit RGBA image, top row first.</summary>
public sealed record Image(int Width, int Height, byte[] Rgba);

/// <summary>Reads the 8-bit RGB or RGBA, non-interlaced PNGs the renderer writes, with any of the five scanline filters.</summary>
public static class PngReader
{
    public static Image Read(string path) => Decode(File.ReadAllBytes(path));

    public static Image Decode(ReadOnlySpan<byte> png)
    {
        // Create a view of our data
        var img = SKImage.FromEncodedData(png);
        if (img == null) {
            throw new ArgumentException("Failed to decode the input data (most likely not a supported format)");
        }
        int width = img.Width, height = img.Height, channels = 4;

        SKImageInfo info = new SKImageInfo(width, height, SKColorType.Rgba8888);
        byte[] rgba = new byte[width * height * channels];
        unsafe {
            fixed (byte* ptr = rgba) {
                img.ReadPixels(info, (IntPtr)ptr);
            }
        }
        
        return new Image(width, height, rgba);
    }
}
