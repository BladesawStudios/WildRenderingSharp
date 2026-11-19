namespace KingRenderingSharp.Core.Storage;

/// <summary>The stores a renderer draws its shared data from, and where it keeps the program binaries it links.</summary>
/// <param name="Shaders">Programs by name (<c>terrain_prog2_extracted.frag</c>) and the material blocks and tables that go with them.</param>
/// <param name="DeferredMaterials">The material block of each deferred pass (<c>chara_skin.gsys_material.bin</c>).</param>
/// <param name="SystemTextures">The sky and noise textures (<c>SunDisc.r8</c> with its <c>.dims.txt</c>).</param>
/// <param name="Effects">The effect data: particle emitters, the field haze.</param>
/// <param name="ProgramCache">A folder to keep linked program binaries in, or null for none.</param>
public sealed record ContentStores(IContentFiles Shaders, IContentFiles DeferredMaterials, IContentFiles SystemTextures, IContentFiles Effects, string? ProgramCache)
{
    /// <summary>The stores of a cache folder prepared the older way.</summary>
    public static ContentStores FromCache(CacheLayout cache) => new(
        new FolderContentFiles(cache.Shaders), new FolderContentFiles(cache.DeferredMaterials), new FolderContentFiles(cache.SystemTextures),
        new FolderContentFiles(cache.Effects), Path.Combine(cache.Root, "_glprograms"));

    /// <summary>The stores of a folder of shaders alone, with its neighbours named as a cache names them.</summary>
    public static ContentStores FromShaders(string shadersDirectory)
    {
        string root = Path.GetDirectoryName(shadersDirectory) ?? shadersDirectory;
        return FromCache(new CacheLayout(root));
    }
}
