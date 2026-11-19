using System.Text.Json;
using PtclSharp;
using PtclSharp.Layout;
using KingRenderingSharp.Games.Totk.Effects;
using KingRenderingSharp.Core.Rom;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Preparation.Totk.Effects;

/// <summary>Which emitter of which effect file to export, and the id its files are stored under.</summary>
internal sealed record TotkEffectEmitter(string File, string Set, string Emitter, string Id, bool Textured = false);

/// <summary>
/// Exports one emitter of a TotK effect file into the cache: its decompiled program, its static block, its mesh and the manifest of the values the
/// live half needs, all read from the romfs.
/// </summary>
internal static class TotkEffectExport
{
    public static void Run(IRomAccess rom, TotkEffectEmitter emitter, CacheLayout cache)
    {
        var file = TotkEffectFile.Open(rom, emitter.File);
        byte[] data = file.EmitterData(emitter.Set, emitter.Emitter);
        Span<byte> block = data;
        var fields = new StructView(PtclLayouts.For(PtclVersion.TotK_NintendoWareVfx2_15_3_1).Emitter, block);
        TotkEmitterConstants.Apply(fields);

        var (resource, modelIds) = file.Models();
        uint model = (uint)fields.GetUInt64("g3d_primitive_idx");
        int modelIndex = modelIds.ToList().IndexOf(model);
        if (modelIndex < 0)
            throw new InvalidDataException($"Emitter '{emitter.Emitter}' uses model {model:X8}, which its effect file does not hold.");
        var mesh = TotkEffectMesh.Read(resource, modelIndex);

        Directory.CreateDirectory(cache.Effects);
        mesh.Write(Path.Combine(cache.Effects, ParticleEmitterManifest.MeshFile(emitter.Id)));
        File.WriteAllBytes(Path.Combine(cache.Effects, ParticleEmitterManifest.StaticBlockFile(emitter.Id)), data[..ParticleEmitterManifest.StaticBlockBytes]);
        if (file.Plugin(emitter.Set, emitter.Emitter, "EP04", ParticleEmitterManifest.AreaLoopBytes) is { } areaLoop)
            File.WriteAllBytes(Path.Combine(cache.Effects, ParticleEmitterManifest.PluginFile(emitter.Id)), areaLoop);
        if (fields.GetUInt64("tex_slot0_guid") is var texture and not ulong.MaxValue)
            TotkEffectTexture.Export(rom, file.TextureName((uint)texture), Path.Combine(cache.Effects, ParticleEmitterManifest.TextureFile(emitter.Id)));
        System.IO.File.WriteAllBytes(Path.Combine(cache.Effects, ParticleEmitterManifest.ManifestFile(emitter.Id)), Manifest(fields, mesh).ToBytes());
        TotkEffectProgram.Export(file.ShaderArchive(), fields.GetInt32("shader_idx_normal"), ParticleEmitterManifest.ProgramName(emitter.Id), cache.Shaders);
    }

    static ParticleEmitterManifest Manifest(StructView fields, TotkEffectMesh mesh) => new(
        (int)fields.GetUInt32("particle_lifespan"),
        Vector(fields, "particle_scale_xyz"), Vector(fields, "emitter_trans_xyz"), Vector(fields, "emitter_scale_xyz"),
        [.. Vector(fields, "emitter_color0_rgb"), fields.GetSingle("emitter_color0_alpha")],
        fields.GetByte("blend_mode_index"), fields.GetByte("cull_mode_index"), mesh.VertexCount, mesh.Indices.Length, mesh.TriangleStrip,
        Vector(fields, "shape_radius_xyz"),
        // One emission every interval plus one frames, each of the emit rate's particles, and each lives its lifespan.
        fields.GetUInt32("particle_lifespan") * fields.GetSingle("emit_rate") / (fields.GetInt32("emit_interval") + 1f));

    static float[] Vector(StructView fields, string name) => [fields.GetSingle(name, 0), fields.GetSingle(name, 1), fields.GetSingle(name, 2)];
}
