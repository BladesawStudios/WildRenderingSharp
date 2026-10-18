namespace WildRenderingSharp.Storage;

/// <summary>
/// Where prepared data lives on disk - the one contract between the offline half (<c>WildRenderingSharp.Preparation</c>, which
/// writes it) and the live half (this assembly, which reads it and parses nothing else).
/// </summary>
public sealed record CacheLayout(string Root)
{
    // Where Default keeps the cache: set once by an app that wants its own folder, or named by WRS_CACHE; otherwise the library's folder in the user's application data.
    public static string DefaultRoot
    {
        get => _defaultRoot ??= Environment.GetEnvironmentVariable("WRS_CACHE") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WildRenderingSharp", "cache");
        set => _defaultRoot = value;
    }

    static string? _defaultRoot;

    public static CacheLayout Default => new(DefaultRoot);

    // Where the textures of every prepared model are kept, once each.
    public string Textures => Path.Combine(Root, CacheFiles.TextureFolder);

    public string Shaders => Path.Combine(Root, "_shaders");

    public string DeferredMaterials => Path.Combine(Root, "_deferred_materials");

    public string SystemTextures => Path.Combine(Root, "_system_textures");

    public string GrassShaders => Path.Combine(Root, "_grass_shaders");

    public string Effects => Path.Combine(Root, "_effects");


    public string Bake => Path.Combine(Root, "_bake");

    public string ModelDirectory(string resolvedModelName) => Path.Combine(Root, resolvedModelName);

    public string ManifestPath(string resolvedModelName) =>
        Path.Combine(ModelDirectory(resolvedModelName), $"{resolvedModelName}.manifest.json");

    public bool IsPrepared(string resolvedModelName) => File.Exists(ManifestPath(resolvedModelName));

    public bool HasSystemAssets => File.Exists(Path.Combine(Shaders, "agl_hdr_compose.vert"));
}
