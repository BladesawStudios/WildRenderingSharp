using System.Diagnostics;

namespace WildRenderingSharp.Storage;

/// <summary>Finds the bundle a path in a model's folder is kept in, reading each bundle from disk once for as long as it is being used.</summary>
/// <remarks>
/// A model is loaded by reading many of its files one after another, so the bundle is held in memory for a few seconds after its last use, and the oldest are let go when the
/// total passes a limit. A folder with no bundle (a model prepared before bundles, or one whose bundle is not written yet) is remembered as having none for the same few
/// seconds, so a loose model is not asked about on every file. Safe to call from any thread.
/// </remarks>
internal static class ModelBundles
{
    // How long what was found is trusted before the disk is asked again: long enough for a model to load, short enough that a model prepared again is seen.
    static readonly long Lifetime = Stopwatch.Frequency * 3;

    // The most bundle bytes held at once.
    const long Capacity = 96L << 20;

    sealed class Held(ModelBundle? bundle, long at)
    {
        public ModelBundle? Bundle { get; } = bundle;
        public long At { get; set; } = at;
    }

    static readonly Dictionary<string, Held> Folders = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Gate = new();
    static long _bytes;

    // The bundle holding the file at path and the entry's name in it, or null when the file is not in one.
    public static (ModelBundle Bundle, string Name)? Locate(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null)
            return null;

        string name = Path.GetFileName(path);
        if (OpenIn(folder) is { } own && own.Contains(name))
            return (own, name);

        // The material blocks live in a folder of their own, inside the model's.
        if (Path.GetFileName(folder) == MaterialFolder && Path.GetDirectoryName(folder) is { } model && OpenIn(model) is { } parent)
        {
            string inParent = $"{MaterialFolder}/{name}";
            if (parent.Contains(inParent))
                return (parent, inParent);
        }
        return null;
    }

    // The folder of a model's material blocks, and its entries' prefix in the bundle.
    public const string MaterialFolder = "matubo";

    // The bundle in a model's own folder, or null when it has none.
    public static ModelBundle? OpenIn(string folder)
    {
        long now = Stopwatch.GetTimestamp();
        lock (Gate)
        {
            if (Folders.TryGetValue(folder, out Held? held) && now - held.At < Lifetime)
            {
                held.At = now;
                return held.Bundle;
            }
        }

        // Read outside the lock: it is a file read, and loaders on other threads are reading other models.
        ModelBundle? bundle = ModelBundle.Open(Path.Combine(folder, ModelBundle.FileName));
        lock (Gate)
        {
            if (Folders.Remove(folder, out Held? old) && old.Bundle is not null)
                _bytes -= old.Bundle.Size;
            Folders[folder] = new Held(bundle, now);
            if (bundle is not null)
                _bytes += bundle.Size;
            if (_bytes > Capacity || Folders.Count > 4096)
                Trim(now);
        }
        return bundle;
    }

    // Lets go of what has not been used for the lifetime, and then of the oldest until the bundles fit.
    static void Trim(long now)
    {
        foreach (var (folder, held) in Folders.Where(f => now - f.Value.At >= Lifetime).ToList())
            Drop(folder, held);
        while (_bytes > Capacity && Folders.Count > 0)
        {
            var oldest = Folders.MinBy(f => f.Value.At);
            Drop(oldest.Key, oldest.Value);
        }
    }

    static void Drop(string folder, Held held)
    {
        Folders.Remove(folder);
        if (held.Bundle is not null)
            _bytes -= held.Bundle.Size;
    }

    // Forgets what is known about a folder, for the moment a bundle is written into it.
    public static void Forget(string folder)
    {
        lock (Gate)
        {
            if (Folders.Remove(folder, out Held? held))
                Drop(folder, held);
        }
    }
}
