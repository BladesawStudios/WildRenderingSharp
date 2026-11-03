using System.Text;

namespace WildRenderingSharp.Storage;

/// <summary>The small files of one prepared model, kept together in a single file.</summary>
/// <remarks>
/// A model is dozens of files - five for each shape, two for each material, a texture index - and on a disk with a virus scanner each one costs a create, a move and a read of
/// its own, which was most of the time a cold cache took to make and to load. A bundle is one file holding them all: a header naming each entry, its place and whether it is
/// compressed, then the entries one after another. Entries are named by their path in the model's folder with forward slashes (<c>matubo/Body.params.json</c>), so a bundle
/// answers the same questions the folder did. It is written whole and never changed; a model prepared again gets a new one.
/// </remarks>
internal sealed class ModelBundle
{
    // The bundle's name inside a model's folder.
    public const string FileName = "model.wrsb";

    const uint Magic = 0x42535257;
    const uint Version = 1;

    readonly byte[] _data;
    readonly Dictionary<string, (int Offset, int Length, bool Packed)> _entries = new(StringComparer.Ordinal);

    ModelBundle(byte[] data) => _data = data;

    // The bytes the bundle holds, which is what keeping it in memory costs.
    public int Size => _data.Length;

    public IEnumerable<string> Names => _entries.Keys;

    public bool Contains(string name) => _entries.ContainsKey(name);

    // The entry's bytes as they were handed in, unpacked; null when the bundle does not have it.
    public byte[]? Read(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
            return null;
        var span = new ReadOnlySpan<byte>(_data, entry.Offset, entry.Length);
        return entry.Packed ? CacheFiles.Unpack(span) : span.ToArray();
    }

    // The entry as it is stored, so a bundle read to be added to keeps its entries without unpacking and packing them again.
    public (byte[] Bytes, bool Packed)? ReadStored(string name) =>
        _entries.TryGetValue(name, out var entry) ? (_data.AsSpan(entry.Offset, entry.Length).ToArray(), entry.Packed) : null;

    // Null when there is no bundle at the path, or it is not one this build can read.
    public static ModelBundle? Open(string path)
    {
        byte[] data;
        try
        {
            data = File.Exists(path) ? File.ReadAllBytes(path) : [];
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        if (data.Length == 0)
            return null;

        try
        {
            using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != Version)
                return null;

            var bundle = new ModelBundle(data);
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string name = reader.ReadString();
                int offset = reader.ReadInt32(), length = reader.ReadInt32();
                bool packed = reader.ReadBoolean();
                if (offset < 0 || length < 0 || offset + length > data.Length)
                    return null;
                bundle._entries[name] = (offset, length, packed);
            }
            return bundle;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            // A bundle cut short by a crash is no bundle: the model is prepared again.
            return null;
        }
    }

    // Writes the entries as a bundle, all at once, so a reader finds the old one or the new one and never half of either.
    public static void Write(string path, IReadOnlyDictionary<string, (byte[] Bytes, bool Packed)> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(entries.Count);

            // The header's size is known once the names are, so the offsets can be written in one pass.
            int headerSize = 12 + entries.Sum(e => NameSize(e.Key) + 9);
            int offset = headerSize;
            foreach (var (name, entry) in entries)
            {
                writer.Write(name);
                writer.Write(offset);
                writer.Write(entry.Bytes.Length);
                writer.Write(entry.Packed);
                offset += entry.Bytes.Length;
            }
            foreach (var entry in entries.Values)
                writer.Write(entry.Bytes);
        }
        AtomicFile.WriteAllBytes(path, stream.GetBuffer().AsSpan(0, (int)stream.Length));
    }

    // What BinaryWriter writes for a string: its length in 7-bit groups, then its UTF-8.
    static int NameSize(string name)
    {
        int bytes = Encoding.UTF8.GetByteCount(name), prefix = 1;
        for (int rest = bytes; rest >= 0x80; rest >>= 7)
            prefix++;
        return prefix + bytes;
    }
}
