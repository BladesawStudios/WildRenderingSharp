using System.Runtime.InteropServices;
namespace WildRenderingSharp.Imaging;

/// <summary>
/// Hand-rolled DDS structures.
/// </summary>
/// <remarks>
/// This is hand-rolled because SkiaSharp doesn't support DDS. It does have KTX
/// (Khronos' texture format), but it doesn't seem like it allows for passthrough
/// of BC data in the way we need.
///
/// There are a few other libraries related to DDS, which mostly focus on doing
/// CPU decoding of the BC data (namely Pfim, and asarium's DDSReader).
///
/// Microsoft provides a "DDSTextureLoader" in DirectXTex and DirectXTK, but
/// these bundle a ton of other stuff we don't need, along with duplicate
/// decoders for common formats like PNG which we already have via SkiaSharp.
///
/// Since DDS is relatively simple and there don't seem to be any libraries to
/// just write/parse DDS headers and pass around the texture data as-is, I've
/// decided to write our own.
/// -- torf
/// </remarks>
public class DDSStructures {
    // See dwFlags on:
    // https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header
    [Flags]
    public enum DDSD : uint {
        CAPS        = 1 << 0,
        HEIGHT      = 1 << 1,
        WIDTH       = 1 << 2,
        PITCH       = 1 << 3,
        PIXELFORMAT = 1 << 12,
        MIPMAPCOUNT = 1 << 17,
        LINEARSIZE  = 1 << 19,
        DEPTH       = 1 << 23,
        
        REQUIRED_BASE_FLAGS = CAPS | HEIGHT | WIDTH | PIXELFORMAT,
    }

    // See https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-pixelformat
    [Flags]
    public enum PixelFormatFlags : uint {
        ALPHAPIXELS = 1 << 0,
        ALPHA       = 1 << 1,
        FOURCC      = 1 << 2,
        RGB         = 1 << 6,
        YUV         = 1 << 9,
        LUMINANCE   = 1 << 17,
    }

    public enum FourCCValue : uint {
        DXT1 = 0x31545844, // aka. BC1
        DXT2 = 0x32545844, // aka. BC2 (premultiplied alpha)
        DXT3 = 0x33545844, // also BC2 (alpha not premultiplied)
        DXT4 = 0x34545844, // aka. BC3 (premultiplied alpha)
        DXT5 = 0x35545844, // also BC3 (alpha not premultiplied)
        ATI1 = 0x31495441, // aka. BC4
        ATI2 = 0x32495441, // aka. BC5
    }

    public static class Constants {
        public const uint DDSMagic = 0x20534444; // 'DDS ' in ASCII
        public const uint HeaderSize = 0x7C;
        public const uint PixelformatSize = 32;
    }
    
    // For a compressed texture (like DXTn), pitch is the size in bytes of the
    // texture at mip level 0.
    internal static uint DXTPitch(uint height, uint width, uint block_size) {
        const uint block_res = 16; // 16 pixels per block
        float pixels_per_byte = (float)block_res / block_size;
        var pitch = (width * height) / pixels_per_byte;
        return (uint)pitch;
    }
 
    
    [StructLayout(LayoutKind.Sequential), Serializable]
    public struct DDSPixelFormat() {
        public uint Size = Constants.PixelformatSize; // Header size
        public PixelFormatFlags Flags = 0;
        public FourCCValue FourCC = 0;
        public uint RGBBitCount = 0;
        public uint RBitMask = 0;
        public uint GBitMask = 0;
        public uint BBitMask = 0;
        public uint ABitMask = 0;

        public string GetFourCC() {
            string fourcc = "";
            unsafe {
                // Interpret the FOURCC as ASCII
                System.Diagnostics.Debug.Assert(sizeof(FourCCValue) == 4);
                fixed (void* ptr = &FourCC) {
                    fourcc = System.Text.Encoding.ASCII.GetString((byte*)ptr, sizeof(FourCCValue));
                }
            }

            return fourcc;
        }
    }
    
    [StructLayout(LayoutKind.Sequential, Size=0x80), Serializable]
    public struct DDSHeader() {
        public DDSHeader(uint height, uint width, bool hasMips) : this() {
            if (hasMips) {
                // We add 2 to account for 1x1 and 0x0 mip levels
                var mipCount = (uint)Math.Log2(Math.Min(height, width)) + 2;
                Flags |= DDSD.MIPMAPCOUNT;
                MipMapCount = mipCount;
            }
            Height = height;
            Width = width;
        }

        /// <summary>
        /// Construct a DDS header for a known compressed pixel format
        /// </summary>
        /// <param name="height">Height in pixels</param>
        /// <param name="width">Width in pixels</param>
        /// <param name="hasMips">Whether to store mipmaps</param>
        /// <param name="format">The FourCC value to be set as the format</param>
        public DDSHeader(uint height, uint width, bool hasMips, FourCCValue format)
        : this(height, width, hasMips)
        {
            uint blockSize = format switch {
                FourCCValue.DXT1 => 8,  // BC1
                FourCCValue.DXT2 => 16, FourCCValue.DXT3 => 16, // BC2
                FourCCValue.DXT4 => 16, FourCCValue.DXT5 => 16, // BC3
                FourCCValue.ATI1 => 8,  // BC4
                FourCCValue.ATI2 => 16, // BC5
            };
            Flags |= DDSD.LINEARSIZE;
            PitchOrLinearSize = DXTPitch(height, width, blockSize);
            
            ddspf.Flags |= PixelFormatFlags.FOURCC;
            ddspf.FourCC = format;
        }
        
        /// <summary>
        /// Construct a DDS header for an uncompressed pixel format
        /// </summary>
        /// <param name="height">Height in pixels</param>
        /// <param name="width">Width in pixels</param>
        /// <param name="hasMips">Whether to store mipmaps</param>
        /// <param name="channels">The number of channels (assumed 8-bit, RGBA order)</param>
        public DDSHeader(uint height, uint width, bool hasMips, uint channels)
            : this(height, width, hasMips)
        {
            const uint bitsPerChannel = 8;
            Flags |= DDSD.PITCH;
            ddspf.RGBBitCount = bitsPerChannel * channels;
            // I don't completely understand this formula, but it works and the one
            // I wrote myself didn't. It comes from MSDN:
            // https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide
            // -- torf
            PitchOrLinearSize = (width * ddspf.RGBBitCount + 7) / 8;
            
            // 0xFF for 8-bit, 0xFFFF for 16-bit, etc.
            var channel_mask = UInt32.MaxValue >> (int)(32 - bitsPerChannel);
            
            if (channels >= 4) {
                ddspf.ABitMask = channel_mask << (3 * (int)bitsPerChannel);
            }
            if (channels >= 3) {
                ddspf.BBitMask = channel_mask << (2 * (int)bitsPerChannel);
            }
            if (channels >= 2) {
                ddspf.GBitMask = channel_mask << (1 * (int)bitsPerChannel);
            }
            if (channels >= 1) {
                ddspf.RBitMask = channel_mask << (0 * (int)bitsPerChannel);
            }

            if (channels == 4) {
                // Swap red & blue channels
                (ddspf.RBitMask, ddspf.BBitMask) = (ddspf.BBitMask, ddspf.RBitMask);
                ddspf.Flags |= PixelFormatFlags.ALPHA; // Enable alpha
            }

            if (channels == 4 || channels == 3) {
                ddspf.Flags |= PixelFormatFlags.RGB; // Enable RGB
            }

            // The only way to get 2-channel textures is using the red & alpha channels
            if (channels == 2) {
                // Move 2nd channel to alpha & enable alpha channel
                ddspf.ABitMask = ddspf.GBitMask;
                ddspf.GBitMask = 0;
                ddspf.Flags |= PixelFormatFlags.ALPHAPIXELS;
            }

            if (channels == 2 || channels == 1) {
                // Luminance goes in red channel
                ddspf.Flags |= PixelFormatFlags.LUMINANCE;
            }
        }

        public readonly uint Magic = Constants.DDSMagic;
        public readonly uint Size = Constants.HeaderSize; // Size of the header
        public readonly DDSD Flags = DDSD.REQUIRED_BASE_FLAGS;
        public readonly uint Height = 0;
        public readonly uint Width = 0;
        public readonly uint PitchOrLinearSize = 0;
        public readonly uint Depth = 0;
        public readonly uint MipMapCount = 0;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public readonly uint[] dwReserved1;
        
        public readonly DDSPixelFormat ddspf;
        public readonly uint Caps = 0;
        public readonly uint Caps2 = 0;
        public readonly uint Caps3 = 0;
        public readonly uint Caps4 = 0;
        public readonly uint Reserved2 = 0;
    }
}