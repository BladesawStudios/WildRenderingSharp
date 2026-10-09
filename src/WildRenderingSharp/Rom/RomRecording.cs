using System.Collections.Concurrent;

namespace WildRenderingSharp.Rom;

/// <summary>
/// The files a <see cref="LayeredRom"/> was asked for while it was active, on this async flow only, and the on-disk file that won each.
/// A lookup that found nothing is recorded too, so a layer that later adds the file shows up as a change.
/// </summary>
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

    /// <summary>Game-relative path to the winning file, or null when no layer had it.</summary>
    public IReadOnlyDictionary<string, string?> Files => _files;

    internal void Note(string path, string? winner) => _files[path] = winner;

    public void Dispose() => _slot.Value = _previous;
}
