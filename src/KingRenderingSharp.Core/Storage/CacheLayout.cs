namespace KingRenderingSharp.Core.Storage;

/// <summary>
/// Where prepared data lives on disk - the one contract between the offline half (<c>KingRenderingSharp.Packing</c>, which
/// writes it) and the live half (this assembly, which reads it and parses nothing else): the system pack, a pack and its shader archives for each model, and the bake packs.
/// </summary>
public sealed record CacheLayout(string Root)
{
    // Where Default keeps the cache: set once by an app that wants its own folder, or named by KRS_CACHE; otherwise the library's folder in the user's application data.
    public static string DefaultRoot
    {
        get => Environment.GetEnvironmentVariable("KRS_CACHE") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KingRenderingSharp", "cache");
    }

    public static CacheLayout Default => new(DefaultRoot);

    public string Bake => Path.Combine(Root, "_bake");

    public string ModelDirectory(string resolvedModelName) => Path.Combine(Root, resolvedModelName);

    // Where the packs of the prepared models are kept: <name>.bfap, and beside it <name>.shaders with the prepared archives of the shaders it uses.
    public string Packs => Path.Combine(Root, "_packs");

    public string ModelPack(string resolvedModelName) => Path.Combine(Packs, resolvedModelName + ".bfap");

    public string ModelShaders(string resolvedModelName) => Path.Combine(Packs, resolvedModelName + ".shaders");

    // The pack of the data that belongs to no model: programs, system textures, effects.
    public string SystemPack => Path.Combine(Root, "system.bfgd");

    public bool IsPrepared(string resolvedModelName) => File.Exists(ModelPack(resolvedModelName));

    public bool HasSystemAssets => File.Exists(SystemPack);
}
