namespace KingRenderingSharp.Core.Storage;

/// <summary>The stores a renderer draws its shared data from, and where it keeps the program binaries it links.</summary>
/// <param name="Shaders">Programs by name (<c>terrain_prog2_extracted.frag</c>) and the material blocks and tables that go with them.</param>
/// <param name="DeferredMaterials">The material block of each deferred pass (<c>chara_skin.gsys_material.bin</c>).</param>
/// <param name="SystemTextures">The sky and noise textures (<c>SunDisc.tex</c>).</param>
/// <param name="Effects">The effect data: particle emitters, the field haze.</param>
/// <param name="ProgramCache">A folder to keep linked program binaries in, or null for none.</param>
public sealed record ContentStores(IContentFiles Shaders, IContentFiles DeferredMaterials, IContentFiles SystemTextures, IContentFiles Effects, string? ProgramCache);
