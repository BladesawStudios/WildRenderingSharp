using WildRenderingSharp.Profiles.Totk.Effects;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>
/// The effect the game draws around the camera to fill its volume mask: a cylinder of the main field's low fog, which the field pre-shading
/// reads to weigh the palette's low fog and volume tint.
/// </summary>
public static class TotkVolumeMaskEffect
{
    static readonly TotkEffectEmitter[] Emitters =
    [
        new("static", "Env_VolumeMask_Fog_MainField", "Haze_Far_simple_new", ParticleEmitterManifest.VolumeMaskId),
        new("static", "Env_VolumeMask_AddFog_MainField", "Haze_Far", ParticleEmitterManifest.AddFogId, Textured: true),
    ];

    public static bool IsExported(CacheLayout cache) => Emitters.All(e => ParticleEmitterManifest.IsPrepared(cache.Shaders, cache.Effects, e.Id) && (!e.Textured || File.Exists(ParticleEmitterManifest.TexturePath(cache.Effects, e.Id))));

    public static void Export(IRomAccess rom, CacheLayout cache)
    {
        foreach (var emitter in Emitters)
            TotkEffectExport.Run(rom, emitter, cache);
    }
}
