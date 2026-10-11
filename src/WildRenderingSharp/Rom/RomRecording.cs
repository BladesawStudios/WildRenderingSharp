using System.Collections.Concurrent;

namespace WildRenderingSharp.Rom;

/// <summary>The on-disk files a LayeredRom answered lookups with while it was active on this async flow, misses included.</summary>
public sealed class RomRecording : IDisposable
{
    readonly ConcurrentDictionary<string, string?> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly AsyncLocal<RomRecording?> _slot;
    readonly RomRecording? _previous;

    internal RomRecording(AsyncLocal<RomRecording?> slot)
    {
        _slot = slot;
        _previous = slot.Value;
        slot.Value = this;
    }

    public IReadOnlyDictionary<string, string?> Files => _files;

    internal void Note(string path, string? winner) => _files[path] = winner;

    public void Dispose() => _slot.Value = _previous;
}
