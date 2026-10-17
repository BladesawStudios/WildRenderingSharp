using System.Runtime.InteropServices;
using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>A prepared particle emitter read back from the cache: its manifest, its static block, and its mesh.</summary>
internal sealed record ParticleEmitterAsset(
    string Id, ParticleEmitterManifest Manifest, byte[] StaticBlock, float[] Positions, float[] Colors, float[] Uvs, uint[] Indices, AreaLoop? AreaLoop)
{
    // Null when the emitter was not prepared, which leaves the feature that needs it off.
    public static ParticleEmitterAsset? Load(string shadersDirectory, string effectsDirectory, string id)
    {
        if (!ParticleEmitterManifest.IsPrepared(shadersDirectory, effectsDirectory, id))
            return null;

        var manifest = JsonSerializer.Deserialize<ParticleEmitterManifest>(File.ReadAllText(ParticleEmitterManifest.ManifestPath(effectsDirectory, id)))
            ?? throw new InvalidDataException($"The manifest of effect '{id}' is empty.");
        byte[] block = File.ReadAllBytes(ParticleEmitterManifest.StaticBlockPath(effectsDirectory, id));
        byte[] mesh = File.ReadAllBytes(ParticleEmitterManifest.MeshPath(effectsDirectory, id));

        // The mesh file holds the positions, then the colours, then the texture coordinates, then the indices.
        int positionBytes = manifest.VertexCount * 3 * sizeof(float), colorBytes = manifest.VertexCount * 4 * sizeof(float), uvBytes = manifest.VertexCount * 2 * sizeof(float);
        if (block.Length != ParticleEmitterManifest.StaticBlockBytes || mesh.Length != positionBytes + colorBytes + uvBytes + manifest.IndexCount * sizeof(uint))
            throw new InvalidDataException($"The files of effect '{id}' do not match its manifest.");
        return new ParticleEmitterAsset(id, manifest, block,
            [.. MemoryMarshal.Cast<byte, float>(mesh.AsSpan(0, positionBytes))],
            [.. MemoryMarshal.Cast<byte, float>(mesh.AsSpan(positionBytes, colorBytes))],
            [.. MemoryMarshal.Cast<byte, float>(mesh.AsSpan(positionBytes + colorBytes, uvBytes))],
            [.. MemoryMarshal.Cast<byte, uint>(mesh.AsSpan(positionBytes + colorBytes + uvBytes))],
            File.Exists(ParticleEmitterManifest.PluginPath(effectsDirectory, id)) ? new AreaLoop(File.ReadAllBytes(ParticleEmitterManifest.PluginPath(effectsDirectory, id))) : null);
    }
}
