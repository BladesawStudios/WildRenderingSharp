using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WildRenderingSharp.Cloth.Format;

public readonly struct TagHeader
{
    public readonly int Size;
    public readonly byte ChunkFlags;
    public readonly string Signature;

    public TagHeader(int size, byte chunkFlags, string signature)
    {
        Size = size;
        ChunkFlags = chunkFlags;
        Signature = signature;
    }

    public static TagHeader Read(ref ReadOnlySpan<byte> span)
    {
        if (span.Length < 8)
            throw new EndOfStreamException("Not enough bytes for TagHeader.");

        uint rawSize = BinaryPrimitives.ReadUInt32BigEndian(span[..4]);
        byte chunkFlags = (byte)(rawSize >> 30);
        int size = (int)(rawSize & 0x3FFFFFFF);
        string signature = Encoding.ASCII.GetString(span.Slice(4, 4));

        span = span[8..];
        return new TagHeader(size, chunkFlags, signature);
    }
}

public sealed class TagItem
{
    public ushort Flags { get; init; }
    public ushort TypeIndex { get; init; }
    public int DataOffset { get; init; }
    public uint Count { get; init; }
}

public sealed class TagPatch
{
    public uint TypeIndex { get; init; }
    public uint[] Offsets { get; init; } = Array.Empty<uint>();
}

public sealed class TagNamedType
{
    public int Index { get; init; }
    public string Name { get; set; } = string.Empty;
}

public sealed class TagFile
{
    public string SdkVersion { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public List<TagItem> Items { get; } = new();
    public List<TagPatch> InternalPatches { get; } = new();
    public List<TagPatch> ExternalPatches { get; } = new();

    public List<string> TypeStrings { get; } = new();
    public List<string> FieldStrings { get; } = new();
    public List<TagNamedType> NamedTypes { get; } = new();
    public byte[] TptrData { get; set; } = Array.Empty<byte>();
    public byte[] TbdyData { get; set; } = Array.Empty<byte>();

    public static TagFile FromBytes(ReadOnlyMemory<byte> bytes)
    {
        var tagFile = new TagFile();
        var span = bytes.Span;

        var rootHeader = TagHeader.Read(ref span);
        if (rootHeader.Signature != "TAG0")
            throw new InvalidDataException($"Expected TAG0 signature, got {rootHeader.Signature}");

        // Read sub-sections: SDKV, DATA, TYPE, INDX
        while (span.Length >= 8)
        {
            var sectionHeader = TagHeader.Read(ref span);
            int payloadLength = sectionHeader.Size - 8;
            if (payloadLength < 0 || payloadLength > span.Length)
                break;

            var payload = span[..payloadLength];
            span = span[payloadLength..];

            switch (sectionHeader.Signature)
            {
                case "SDKV":
                    tagFile.SdkVersion = Encoding.ASCII.GetString(payload).TrimEnd('\0');
                    break;

                case "DATA":
                    tagFile.Data = payload.ToArray();
                    break;

                case "TYPE":
                    ParseTypeSection(payload, tagFile);
                    break;

                case "INDX":
                    ParseIndexSection(payload, tagFile);
                    break;
            }
        }

        tagFile.ApplyRelocations();
        return tagFile;
    }

    private static void ParseTypeSection(ReadOnlySpan<byte> span, TagFile tagFile)
    {
        while (span.Length >= 8)
        {
            var header = TagHeader.Read(ref span);
            int payloadLen = header.Size - 8;
            if (payloadLen < 0 || payloadLen > span.Length)
                break;

            var sectionSpan = span[..payloadLen];
            span = span[payloadLen..];

            switch (header.Signature)
            {
                case "TPTR":
                    tagFile.TptrData = sectionSpan.ToArray();
                    break;

                case "TBDY":
                    tagFile.TbdyData = sectionSpan.ToArray();
                    break;

                case "TST1":
                    ReadNullTerminatedStrings(sectionSpan, tagFile.TypeStrings);
                    break;

                case "FST1":
                    ReadNullTerminatedStrings(sectionSpan, tagFile.FieldStrings);
                    break;

                case "TNA1":
                    ParseNamedTypes(sectionSpan, tagFile);
                    break;
            }
        }
    }

    private static void ReadNullTerminatedStrings(ReadOnlySpan<byte> span, List<string> output)
    {
        int start = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == 0)
            {
                if (i > start)
                {
                    output.Add(Encoding.UTF8.GetString(span[start..i]));
                }
                start = i + 1;
            }
        }
    }

    private static void ParseNamedTypes(ReadOnlySpan<byte> span, TagFile tagFile)
    {
        if (span.IsEmpty) return;
        int typeCount = ReadVarUInt(ref span);
        for (int i = 0; i < typeCount - 1 && span.Length > 0; i++)
        {
            int stringIndex = ReadVarUInt(ref span);
            string name = stringIndex >= 0 && stringIndex < tagFile.TypeStrings.Count
                ? tagFile.TypeStrings[stringIndex]
                : string.Empty;

            int templateCount = ReadVarUInt(ref span);
            var tArgs = new List<string>();
            for (int t = 0; t < templateCount; t++)
            {
                int tNameIdx = ReadVarUInt(ref span); // template name
                int tValIdx = ReadVarUInt(ref span); // template value
                string argName = tValIdx >= 0 && tValIdx < tagFile.TypeStrings.Count ? tagFile.TypeStrings[tValIdx] : tValIdx.ToString();
                tArgs.Add(argName);
            }
            if (tArgs.Count > 0)
            {
                name += "<" + string.Join(", ", tArgs) + ">";
            }

            tagFile.NamedTypes.Add(new TagNamedType { Index = i, Name = name });
        }
    }

    public static int ReadVarUInt(ref ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty) return 0;
        byte b0 = span[0];
        span = span[1..];

        if ((b0 & 0x80) == 0)
            return b0;

        int x = b0 >> 3;
        int size = 0;
        if (x is >= 0x10 and <= 0x17) size = 1;
        else if (x is >= 0x18 and <= 0x1B) size = 2;
        else if (x == 0x1C) size = 3;
        else if (x == 0x1D) size = 4;
        else if (x == 0x1E) size = 7;
        else if (x == 0x1F) size = 13;

        int val = b0 & 0x7F;
        for (int i = 0; i < size && span.Length > 0; i++)
        {
            val = (val << 8) | span[0];
            span = span[1..];
        }
        return val;
    }

    private static void ParseIndexSection(ReadOnlySpan<byte> span, TagFile tagFile)
    {
        while (span.Length >= 8)
        {
            var header = TagHeader.Read(ref span);
            int payloadLen = header.Size - 8;
            if (payloadLen < 0 || payloadLen > span.Length)
                break;

            var sectionSpan = span[..payloadLen];
            span = span[payloadLen..];

            switch (header.Signature)
            {
                case "ITEM":
                {
                    int itemCount = payloadLen / 12;
                    for (int i = 0; i < itemCount; i++)
                    {
                        var itemSlice = sectionSpan.Slice(i * 12, 12);
                        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(itemSlice[..2]);
                        ushort typeIndex = BinaryPrimitives.ReadUInt16LittleEndian(itemSlice.Slice(2, 2));
                        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(itemSlice.Slice(4, 4));
                        uint count = BinaryPrimitives.ReadUInt32LittleEndian(itemSlice.Slice(8, 4));

                        tagFile.Items.Add(new TagItem
                        {
                            Flags = flags,
                            TypeIndex = typeIndex,
                            DataOffset = dataOffset,
                            Count = count
                        });
                    }
                    break;
                }

                case "PTCH":
                {
                    var pSpan = sectionSpan;
                    while (pSpan.Length >= 8)
                    {
                        uint typeIndex = BinaryPrimitives.ReadUInt32LittleEndian(pSpan[..4]);
                        if (typeIndex == 0)
                        {
                            pSpan = pSpan[4..];
                            break; // terminator
                        }
                        uint count = BinaryPrimitives.ReadUInt32LittleEndian(pSpan.Slice(4, 4));
                        pSpan = pSpan[8..];

                        if ((long)count * 4 > pSpan.Length)
                            break;

                        var offsets = new uint[count];
                        for (int c = 0; c < count; c++)
                        {
                            offsets[c] = BinaryPrimitives.ReadUInt32LittleEndian(pSpan.Slice(c * 4, 4));
                        }
                        pSpan = pSpan[(int)(count * 4)..];

                        tagFile.InternalPatches.Add(new TagPatch
                        {
                            TypeIndex = typeIndex,
                            Offsets = offsets
                        });
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Applies pointer relocations to the Data buffer, converting serialized item indices
    /// into 64-bit pointers and writing array counts at offset + 8.
    /// </summary>
    public void ApplyRelocations()
    {
        if (Data.Length == 0 || Items.Count == 0)
            return;

        Span<byte> dataSpan = Data.AsSpan();

        foreach (var patch in InternalPatches)
        {
            string typeName = patch.TypeIndex > 0 && patch.TypeIndex <= NamedTypes.Count
                ? NamedTypes[(int)patch.TypeIndex - 1].Name
                : string.Empty;

            bool isArray = typeName.StartsWith("hkArray");

            foreach (uint offset in patch.Offsets)
            {
                if (offset + 8 > dataSpan.Length)
                    continue;

                uint itemIndex = BinaryPrimitives.ReadUInt32LittleEndian(dataSpan.Slice((int)offset, 4));
                if (itemIndex >= Items.Count)
                    continue;

                var item = Items[(int)itemIndex];

                // Write 64-bit target data offset
                long targetOffset = item.DataOffset;
                BinaryPrimitives.WriteInt64LittleEndian(dataSpan.Slice((int)offset, 8), targetOffset);

                // If it's an hkArray, write count at offset + 8
                if (isArray && offset + 12 <= dataSpan.Length)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(dataSpan.Slice((int)offset + 8, 4), (int)item.Count);
                }
            }
        }
    }
}
