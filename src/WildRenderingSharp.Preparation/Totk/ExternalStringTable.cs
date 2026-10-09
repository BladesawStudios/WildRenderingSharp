using System.Collections.Concurrent;
using System.Text;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The shared table naming the keys of TotK's V10 materials, read from the ROM; a scope makes one ROM's table the one BFRES parsing on this
/// async flow uses.
/// </summary>
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

    public static IDisposable Use(IRomAccess rom)
    {
        byte[] table = Mcpk.ToBfres(rom.ReadAllBytesDirectSpan(TablePath).ToArray(), TablePath);
        var previous = Current.Value;
        Current.Value = new ExternalStringTable(table);
        return new Scope(previous);
    }

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
