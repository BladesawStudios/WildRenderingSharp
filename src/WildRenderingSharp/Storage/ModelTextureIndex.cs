namespace WildRenderingSharp.Storage;

/// <summary>Which shared texture each texture file of a model is, read from the model's session, bundle or folder.</summary>
/// <remarks>
/// A model keeps a small <c>textures.idx</c> of file name and hash lines for the textures it shares through the cache's texture folder. A bundle's index never changes, so it is
/// parsed once; a folder's is read afresh each time.
/// </remarks>
internal static class ModelTextureIndex
{
    public const string FileName = "textures.idx";

    // The texture index of a model's folder, from the open session, its bundle or the folder; empty when it has none. Read afresh each time for a folder's own file, and once
    // for a bundle, whose entry never changes.
    internal static IReadOnlyDictionary<string, string> Of(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (ModelFiles.InSession(path) is { } held)
            return held.Read(FileName) is { } fromSession ? Parse(fromSession) : new Dictionary<string, string>();
        if (ModelBundles.OpenIn(folder) is { } bundle)
            return ParsedIndexes.GetValue(bundle, b => b.Read(FileName) is { } bytes ? Parse(bytes) : new Dictionary<string, string>());
        return ReadFile(path);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModelBundle, Dictionary<string, string>> ParsedIndexes = new();

    static Dictionary<string, string> Parse(byte[] bytes)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0)
                index[line[..tab]] = line[(tab + 1)..].TrimEnd('\r');
        }
        return index;
    }

    internal static Dictionary<string, string> ReadFile(string indexPath)
    {
        try
        {
            return File.Exists(indexPath) ? Parse(File.ReadAllBytes(indexPath)) : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A folder with no index has no shared textures, which is an empty index.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
