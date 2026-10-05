namespace WildRenderingSharp;

/// <summary>
/// Where prepared data lives on disk - the one contract between the offline half
/// (<c>WildRenderingSharp.Preparation</c>, which writes it) and the live half (this assembly,
/// which reads it and parses nothing else).
/// </summary>
/// <remarks>
/// <para>
/// Every prepared model gets its own subdirectory under <see cref="Root"/>, named after the
/// RESOLVED model (an actor name may resolve to a differently-named model), so two models that
/// share a shape or texture name never clobber each other's files.
/// </para>
/// <para>
/// The underscore-prefixed directories hold data that is the same whichever model is loaded:
/// decompiled shader programs (keyed by shading model and program index into the one shipped
/// <c>material.bfsha</c>), the deferred-resolve passes' own material blocks, the "system"
/// textures some shaders sample, and the sky LUT. They are built once and shared.
/// </para>
/// <para>
/// Several tools may point at the same root and share one cache; nothing in it is tool-specific.
/// </para>
/// </remarks>
public sealed record CacheLayout(string Root)
{
    /// <summary><c>%AppData%\WildRenderingSharp\cache</c>.</summary>
    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WildRenderingSharp", "cache");

    public static CacheLayout Default { get; } = new(DefaultRoot);

    /// <summary>Decompiled <c>.vert</c>/<c>.frag</c> programs, shared by every model.</summary>
    public string Shaders => Path.Combine(Root, "_shaders");

    /// <summary>The deferred-resolve passes' own <c>gsys_material</c> blocks (<c>chara_skin</c>, <c>chara_hair</c>, ...).</summary>
    public string DeferredMaterials => Path.Combine(Root, "_deferred_materials");

    /// <summary>Static assets behind "system" sampler names (3D noise, cloud masks, sun/moon sprites).</summary>
    public string SystemTextures => Path.Combine(Root, "_system_textures");

    /// <summary>The precomputed sky-scattering LUT (<c>res/master_field.skybin</c>).</summary>
    public string SkyData => Path.Combine(Root, "_sky_data");

    /// <summary>A prepared model's own directory.</summary>
    public string ModelDirectory(string resolvedModelName) => Path.Combine(Root, resolvedModelName);

    /// <summary>The manifest the live half loads a model from.</summary>
    public string ManifestPath(string resolvedModelName) =>
        Path.Combine(ModelDirectory(resolvedModelName), $"{resolvedModelName}.manifest.json");

    /// <summary>True if the model has been prepared into this cache at all (not whether it is up to date with the romfs).</summary>
    public bool IsPrepared(string resolvedModelName) => File.Exists(ManifestPath(resolvedModelName));

    /// <summary>True once the shared system assets the pipeline cannot be constructed without are present.</summary>
    /// <remarks>The pipeline links <c>agl_hdr_compose</c> in its constructor, so that file is the hard requirement; everything else degrades.</remarks>
    public bool HasSystemAssets => File.Exists(Path.Combine(Shaders, "agl_hdr_compose.vert"));
}
