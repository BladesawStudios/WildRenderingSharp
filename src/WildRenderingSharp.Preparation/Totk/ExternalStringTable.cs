using System.Collections.Concurrent;
using System.Text;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// Turns the 64-bit keys that TotK's V10 BFRES files store in place of names back into the names, using a string table from the ROM.
/// </summary>
/// <remarks>
/// Call <see cref="Use"/> with a ROM before parsing a BFRES and dispose the result afterwards; <see cref="Lookup"/> reads from the table it
/// loaded, which is kept per async flow. The table file holds a sorted array of 64-bit keys starting at byte 224, followed by an entries
/// array whose offset the header stores at byte 192. Each entry is 16 bytes and begins with the offset of a length-prefixed UTF-8 string, and a
/// key's string is the entry at the same index as the key.
/// </remarks>
public sealed class ExternalStringTable
{
    const string TablePath = "Shader/ExternalBinaryString.bfres.mc";
    const long KeyArrayStart = 224;
    const long EntriesBaseField = 192;
    const int EntryStride = 16;

    static readonly AsyncLocal<ExternalStringTable?> Current = new();

    readonly byte[] _data;
    readonly ulong[] _keys;
    readonly long _entriesBase;
    readonly ConcurrentDictionary<ulong, string?> _names = new();

    ExternalStringTable(byte[] data)
    {
        // The key array runs from KeyArrayStart up to the entries array, whose offset the header stores.
        _entriesBase = BitConverter.ToInt64(data, (int)EntriesBaseField);
        if (_entriesBase <= KeyArrayStart || _entriesBase > data.Length || (_entriesBase - KeyArrayStart) % 8 != 0)
            throw new InvalidDataException($"ExternalBinaryString entries offset is {_entriesBase}, not a valid key-array end for a {data.Length}-byte table.");

        _data = data;
        _keys = new ulong[(_entriesBase - KeyArrayStart) / 8];
        for (int i = 0; i < _keys.Length; i++)
            _keys[i] = BitConverter.ToUInt64(data, (int)(KeyArrayStart + i * 8));
    }

    // Loads the ROM's table for Lookup to use until the returned scope is disposed, then goes back to the previous table.
    public static IDisposable Use(IRomAccess rom)
    {
        byte[] table = Mcpk.ToBfres(rom.ReadAllBytesDirectSpan(TablePath).ToArray(), TablePath);
        var previous = Current.Value;
        Current.Value = new ExternalStringTable(table);
        return new Scope(previous);
    }

    // The string a key names in the current table, or null if the table has no entry for it.
    public static string? Lookup(ulong key) =>
        (Current.Value ?? throw new InvalidOperationException("No external string table is in use.")).Find(key);

    string? Find(ulong key) => _names.GetOrAdd(key, Resolve);

    string? Resolve(ulong key)
    {
        int index = Array.BinarySearch(_keys, key);
        return index < 0 ? null : ReadString(BitConverter.ToInt64(_data, (int)(_entriesBase + index * EntryStride)));
    }

    string? ReadString(long offset)
    {
        if (offset <= 0 || offset + 2 > _data.Length)
            return null;
        ushort length = BitConverter.ToUInt16(_data, (int)offset);
        return offset + 2 + length > _data.Length ? null : Encoding.UTF8.GetString(_data, (int)offset + 2, length);
    }

    sealed class Scope(ExternalStringTable? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
