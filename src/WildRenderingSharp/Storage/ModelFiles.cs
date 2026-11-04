namespace WildRenderingSharp.Storage;

/// <summary>The small files of a prepared model, found, read and written wherever the model keeps them.</summary>
/// <remarks>
/// A model's small files are in the <see cref="ModelSession"/> being prepared, in its <see cref="ModelBundle"/>, or loose in its folder, and a call here looks in that order.
/// Textures and the other big binary files are <see cref="CacheFiles"/>'s.
/// </remarks>
public static class ModelFiles
{
    // Whether a model is prepared: its bundle is written, which is only once all of it is, or its manifest is in its folder as a file. Asks the disk about one file and reads nothing.
    public static bool IsPrepared(string folder, string manifestName) =>
        File.Exists(Path.Combine(folder, ModelBundle.FileName)) || File.Exists(Path.Combine(folder, manifestName));

    // The names of the files in a model's folder that match the pattern, whether they are in its bundle, in the open session or in the folder. The pattern has one * in it at most.
    public static IEnumerable<string> EnumerateFiles(string folder, string pattern)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (ModelSession.Current is { } session && session.NameOf(Path.Combine(folder, "x")) is not null)
            foreach (string held in session.Names)
                names.Add(held);
        if (ModelBundles.OpenIn(folder) is { } bundle)
            foreach (string stored in bundle.Names)
                names.Add(stored);
        if (Directory.Exists(folder))
            foreach (string file in Directory.EnumerateFiles(folder, pattern))
                names.Add(Path.GetFileName(file));

        int star = pattern.IndexOf('*');
        string head = star < 0 ? pattern : pattern[..star], tail = star < 0 ? "" : pattern[(star + 1)..];
        return names.Where(n => !n.Contains('/') && (star < 0 ? n == pattern : n.Length >= head.Length + tail.Length
            && n.StartsWith(head, StringComparison.Ordinal) && n.EndsWith(tail, StringComparison.Ordinal)));
    }

    // Makes a model's folder for a file that is to be written into it, unless its files go into the open session's bundle, which makes it when it is written.
    public static void EnsureFolder(string path)
    {
        if (InSession(Path.Combine(path, "x")) is null)
            Directory.CreateDirectory(path);
    }

    // Whether the file is in the open session, in the model's bundle, or in its folder as it is.
    public static bool FileExists(string path)
    {
        if (InSession(path) is { } held)
            return held.Contains(held.NameOf(path)!);
        return ModelBundles.Locate(path) is not null || File.Exists(path);
    }

    // The text of a small file of a model, from its bundle or its folder; null when it has none.
    public static string? TryReadText(string path)
    {
        if (InSession(path) is { } held && held.Read(held.NameOf(path)!) is { } fromSession)
            return System.Text.Encoding.UTF8.GetString(fromSession);
        if (ModelBundles.Locate(path) is var (bundle, name))
            return bundle.Read(name) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // Writes a small text file of a model: into the open session when the file is in its folder, otherwise to the folder.
    public static void WriteText(string path, string text)
    {
        if (InSession(path) is { } held)
            held.Add(held.NameOf(path)!, System.Text.Encoding.UTF8.GetBytes(text), packed: false);
        else
            File.WriteAllText(path, text);
    }

    // The open session, when the file is inside its model's folder.
    internal static ModelSession? InSession(string path) => ModelSession.Current is { } session && session.NameOf(path) is not null ? session : null;
}
