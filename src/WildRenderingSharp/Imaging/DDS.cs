using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Imaging;
using static DDSStructures;

/// <summary>
/// Hand-rolled DDS reader/writer.
/// </summary>
public static class DDS {
    public static void WriteRawDDS(Stream output, DDSHeader header, ReadOnlySpan<byte> data)
    {
        // Write DDS header
        unsafe {
            fixed (void* ptr = &header) {
                var headerSpan = new ReadOnlySpan<byte>((byte*)ptr, sizeof(DDSStructures.DDSHeader));
                output.Write(headerSpan);
            }
        }
        
        // Write the texture data
        output.Write(data);
    }

    public static void WriteCompressedDDS(Stream output, uint height, uint width,
                                    FourCCValue format, ReadOnlySpan<byte> data)
    {
        // Build DDS header
        var header = new DDSHeader(height, width, true, format);
        WriteRawDDS(output, header, data);
    }
    
    public static void WriteUncompressedDDS(Stream output, uint height, uint width,
                                        uint channels, ReadOnlySpan<byte> data)
    {
        // Build DDS header
        var header = new DDSHeader(height, width, true, channels);
        WriteRawDDS(output, header, data);
    }

    public static CompressedTextureFormat.Info? GetCompressedFormatDDS(DDSHeader header)
    {
        if (header.ddspf.Flags.HasFlag(PixelFormatFlags.FOURCC)) {
            return null; // Not a compressed format
        }

        string fourcc = header.ddspf.GetFourCC();
        if (fourcc == "DX10") {
            // BC7, for some reason, has no FourCC code and requires the DX10
            // extended header. Wasn't the whole point of the FourCC to be
            // forward compatible...? Everyone say, "Thank you, Microsoft"...
            // -- torf
            
            // TODO: Define DX10 header and add it as an optional parameter so we can handle BC7.
            return null;
        }
        
        // Handle more Microsoft shenanigans.
        string format = fourcc switch {
            "DXT1" => "BC1",
            "DXT2" => "BC2", "DXT3" => "BC2",
            "DXT4" => "BC3", "DXT5" => "BC3",
            "ATI1" => "BC4",
            "ATI2" => "BC5",
            _ => "",
        };

        var info = CompressedTextureFormat.Resolve(format);

        return info;
    }
}