using System;
using System.IO;
using System.Text;

namespace WildRenderingSharp.Cloth.Format;

/// <summary>
/// Parser and container for Tears of the Kingdom .bphcl (Binary Phive Cloth) files.
/// A .bphcl file wraps a Havok TAG0 binary Tagfile and an AAMP parameter metadata block.
/// </summary>
public sealed class BphclFile
{
    private static readonly byte[] ExpectedMagic = Encoding.ASCII.GetBytes("Phive\0");

    public ushort ByteOrderMark { get; private set; }
    public byte FileType { get; private set; } // 3 = Cloth
    public byte MaxSectionCapacity { get; private set; }

    public uint TagfileOffset { get; private set; }
    public uint ParamOffset { get; private set; }
    public uint FileEndOffset { get; private set; }

    public uint TagfileSize { get; private set; }
    public uint ParamSize { get; private set; }
    public uint FileEndSize { get; private set; }

    /// <summary>
    /// Raw byte slice containing the Havok TAG0 Tagfile.
    /// </summary>
    public ReadOnlyMemory<byte> TagfileBytes { get; private set; }

    /// <summary>
    /// Raw byte slice containing the parameter IO block (AAMP).
    /// </summary>
    public ReadOnlyMemory<byte> ParamBytes { get; private set; }

    public static BphclFile FromFile(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        return FromBytes(bytes);
    }

    public static BphclFile FromBytes(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 36)
            throw new InvalidDataException($"Data too small for .bphcl header (size: {span.Length}, expected at least 36 bytes).");

        // Validate magic: "Phive\0"
        if (!span[..6].SequenceEqual(ExpectedMagic))
            throw new InvalidDataException("Invalid .bphcl magic; expected 'Phive\\0'.");

        byte reserve0 = span[6]; // usually 1
        byte reserve1 = span[7]; // usually 0

        ushort bom = BitConverter.ToUInt16(span.Slice(8, 2));
        if (bom != 0xFFFE && bom != 0xFEFF)
            throw new InvalidDataException($"Invalid BOM in .bphcl: 0x{bom:X4}");

        byte fileType = span[10];
        byte maxCap = span[11];

        uint tagfileOffset = BitConverter.ToUInt32(span.Slice(12, 4));
        uint paramOffset = BitConverter.ToUInt32(span.Slice(16, 4));
        uint fileEndOffset = BitConverter.ToUInt32(span.Slice(20, 4));

        uint tagfileSize = BitConverter.ToUInt32(span.Slice(24, 4));
        uint paramSize = BitConverter.ToUInt32(span.Slice(28, 4));
        uint fileEndSize = BitConverter.ToUInt32(span.Slice(32, 4));

        if (tagfileOffset + tagfileSize > span.Length)
            throw new InvalidDataException($"Tagfile slice exceeds file bounds: offset={tagfileOffset}, size={tagfileSize}, length={span.Length}");

        if (paramOffset + paramSize > span.Length)
            throw new InvalidDataException($"Param slice exceeds file bounds: offset={paramOffset}, size={paramSize}, length={span.Length}");

        var tagfileBytes = data.Slice((int)tagfileOffset, (int)tagfileSize);
        var paramBytes = data.Slice((int)paramOffset, (int)paramSize);

        return new BphclFile
        {
            ByteOrderMark = bom,
            FileType = fileType,
            MaxSectionCapacity = maxCap,
            TagfileOffset = tagfileOffset,
            ParamOffset = paramOffset,
            FileEndOffset = fileEndOffset,
            TagfileSize = tagfileSize,
            ParamSize = paramSize,
            FileEndSize = fileEndSize,
            TagfileBytes = tagfileBytes,
            ParamBytes = paramBytes
        };
    }
}
