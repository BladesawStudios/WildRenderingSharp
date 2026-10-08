namespace WildRenderingSharp;

/// <summary>
/// Where prepared data lives on disk - the one contract between the offline half (<c>WildRenderingSharp.Preparation</c>, which
/// writes it) and the live half (this assembly, which reads it and parses nothing else).
/// </summary>
public sealed record CacheLayout(string Root)
{
    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WildRenderingSharp", "cache");

    public static CacheLayout Default { get; } = new(DefaultRoot);

    public string Shaders => Path.Combine(Root, "_shaders");

    public string DeferredMaterials => Path.Combine(Root, "_deferred_materials");

    public string SystemTextures => Path.Combine(Root, "_system_textures");

    public string SkyData => Path.Combine(Root, "_sky_data");

    public string Bake => Path.Combine(Root, "_bake");

    public string ModelDirectory(string resolvedModelName) => Path.Combine(Root, resolvedModelName);

    public string ManifestPath(string resolvedModelName) =>
        Path.Combine(ModelDirectory(resolvedModelName), $"{resolvedModelName}.manifest.json");

    public bool IsPrepared(string resolvedModelName) => File.Exists(ManifestPath(resolvedModelName));

    public bool HasSystemAssets => File.Exists(Path.Combine(Shaders, "agl_hdr_compose.vert"));
}
