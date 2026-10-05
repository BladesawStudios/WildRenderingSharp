namespace WildRenderingSharp.Cloth.Tests;

/// <summary>
/// Where the tests find real game data. These are diagnostics against real exported cloth, which
/// cannot be checked in - prepare the actors they name (Npc_Zelda_Search_Improve,
/// Npc_Zelda_AncientHyrule, Npc_Ganondorf_Miasma) with WildRenderingSharp.Preparation first.
/// </summary>
static class TestData
{
    /// <summary>
    /// <c>WRS_TEST_CACHE</c> if set, else the library's default cache, else the original Marrow
    /// viewer's cache (where the data these tests were written against was first prepared).
    /// </summary>
    public static string CacheRoot { get; } = Resolve();

    static string Resolve()
    {
        if (Environment.GetEnvironmentVariable("WRS_TEST_CACHE") is { Length: > 0 } explicitRoot)
            return explicitRoot;
        if (Directory.Exists(CacheLayout.DefaultRoot) && Directory.EnumerateDirectories(CacheLayout.DefaultRoot, "Npc_Zelda*").Any())
            return CacheLayout.DefaultRoot;
        string marrow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marrow", "cache");
        return Directory.Exists(marrow) ? marrow : CacheLayout.DefaultRoot;
    }

    /// <summary>
    /// A file of the local <c>res/test_cloth</c> sample (an exported actor's Phive folder, kept out of
    /// source control as game data): <c>WRS_TEST_CLOTH</c> if set, else the first <c>res/test_cloth</c>
    /// found walking up from the test's own folder.
    /// </summary>
    public static string TestCloth(string relativePath)
    {
        if (Environment.GetEnvironmentVariable("WRS_TEST_CLOTH") is { Length: > 0 } explicitRoot)
            return Path.GetFullPath(Path.Combine(explicitRoot, relativePath));
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "res", "test_cloth");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(Path.Combine(candidate, relativePath));
        }
        return Path.GetFullPath(Path.Combine("res", "test_cloth", relativePath));
    }
}
