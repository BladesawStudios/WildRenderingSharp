using SkiaSharp;

namespace WildRenderingSharp.Imaging;

/// <summary>Encodes a raw RGBA8 buffer as a PNG</summary>
public static class PngWriter
{
    /// <summary>
    /// Wrapper for the Stream overload of this function.
    /// </summary>
    /// <param name="path">The filename to output to</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if given a negative or 0 size.</exception>
    /// <exception cref="ArgumentException">Thrown if the buffer is too small for the image</exception>
    public static void WriteRgba(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var stream = File.Create(path);
        WriteRgba(stream, width, height, rgba);
    }

    /// <summary>
    /// Encode an in-memory 8-bit RGBA image to an output stream
    /// </summary>
    /// <param name="output">The generic stream to output PNG data to</param>
    /// <param name="rgba">The pixel data, stored as rows, with 8 bits per channel (i.e. 32 bits per pixel)</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if given a negative or 0 size.</exception>
    /// <exception cref="ArgumentException">Thrown if the buffer is too small for the image</exception>
    public static void WriteRgba(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        int sizePixels = width * height;
        int sizeBytes = rgba.Length;
        int expectedSize = sizePixels * 4;
        
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), "An image needs at least one pixel.");
        }
        if (rgba.Length < expectedSize) {
            throw new ArgumentException($"Expected {expectedSize} bytes of RGBA, got {rgba.Length}.", nameof(rgba));
        }
        
        // We need to fully specify this since there's a local namespace named "Debug".
        System.Diagnostics.Debug.Assert(sizeBytes >= expectedSize);
        
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        
        // Smuggle the pointer out so we can create a view of the data without a copy
        nint dataPtr = 0;
        unsafe {
            fixed (void* ptr = rgba) {
                dataPtr = (nint)ptr;
            }
        }
        
        // Create a view of our data
        var data = SKData.Create(dataPtr, sizeBytes);
        var img = SKImage.FromPixels(info, data);

        // Note that PNG ignores the quality parameter since it's lossless.
        var encodedData = img.Encode(SKEncodedImageFormat.Png, 90);
        encodedData.SaveTo(output);
    }
}
