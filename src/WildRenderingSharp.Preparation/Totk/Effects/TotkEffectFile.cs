using BymlSharp;
using PtclSharp;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>A TotK effect file (<c>Effect/*.esetb.byml.zs</c>) read down to its VFXB tree, with the pieces of it an exported emitter needs.</summary>
internal sealed class TotkEffectFile
{
    const string Suffix = ".Nin_NX_NVN.esetb.byml.zs";

    readonly VfxbFile _vfxb;
    readonly Dictionary<uint, string> _textures;

    TotkEffectFile(VfxbFile vfxb, Dictionary<uint, string> textures)
    {
        _vfxb = vfxb;
        _textures = textures;
    }

    public static TotkEffectFile Open(IRomAccess rom, string name)
    {
        byte[] decoded = rom.ReadAllBytesCompressedSpan($"Effect/{name}{Suffix}").ToArray();
        Byml root = BymlFile.FromBinary(decoded).Root;
        Byml binary = root["PtclBin"] ?? throw new InvalidDataException($"Effect/{name} has no PtclBin entry.");
        var textures = new Dictionary<uint, string>();
        if (root["Textures"] is { IsArray: true } list)
            foreach (Byml texture in list.AsArray)
                textures[texture["guid"].UInt] = texture["name"].AsString();
        return new TotkEffectFile(VfxbReader.Read(binary.Binary, VfxbLayouts.TotK), textures);
    }

    // The name of a texture the file's emitters name by id, which is the name of its file in TexToGo.
    public string TextureName(uint guid) =>
        _textures.TryGetValue(guid, out string? name) ? name : throw new InvalidDataException($"The effect file lists no texture {guid:X8}.");

    // The emitter's data block, which every field of its layout is read from.
    public byte[] EmitterData(string setName, string emitterName)
    {
        VfxbEmitterSet set = _vfxb.EmitterSets.FirstOrDefault(s => s.Name == setName)
            ?? throw new InvalidDataException($"The effect file has no emitter set '{setName}'.");
        VfxbEmitter emitter = set.Emitters.FirstOrDefault(e => e.Name == emitterName)
            ?? throw new InvalidDataException($"Emitter set '{setName}' has no emitter '{emitterName}'.");
        int start = emitter.Node.DataOffset ?? throw new InvalidDataException($"Emitter '{emitterName}' has no data.");
        return _vfxb.Data.AsSpan(start, VfxbLayouts.TotK.EmitterFixedDataSize).ToArray();
    }

    // The payload of the emitter's plugin chunk of the given kind (such as the area loop's EP04), or null when it has none.
    public byte[]? Plugin(string setName, string emitterName, string kind, int size)
    {
        VfxbEmitter emitter = _vfxb.EmitterSets.First(s => s.Name == setName).Emitters.First(e => e.Name == emitterName);
        VfxbNode? chunk = emitter.Node.Attributes.FirstOrDefault(c => c.Kind == kind);
        return chunk?.DataOffset is int start ? _vfxb.Data.AsSpan(start, size).ToArray() : null;
    }

    // The names of the emitters in a set, in file order.
    public IEnumerable<string> EmitterNames(string setName) =>
        (_vfxb.EmitterSets.FirstOrDefault(s => s.Name == setName) ?? throw new InvalidDataException($"The effect file has no emitter set '{setName}'.")).Emitters.Select(e => e.Name);

    // The shader archive (a BNSH file) in the file's graphics shader node.
    public byte[] ShaderArchive()
    {
        VfxbNode shaders = _vfxb.Roots.FirstOrDefault(r => r.Kind == "GRSN") ?? throw new InvalidDataException("The effect file has no shader node.");
        VfxbNode archive = shaders.Children.FirstOrDefault(c => c.Kind == "GRSR") ?? throw new InvalidDataException("The shader node has no graphics shader file.");
        return Slice(archive);
    }

    // The G3D resource (a BFRES file) holding every model the file's emitters use, and the model ids in its order.
    public (byte[] Resource, IReadOnlyList<uint> Ids) Models()
    {
        VfxbNode models = _vfxb.Roots.FirstOrDefault(r => r.Kind == "G3PR") ?? throw new InvalidDataException("The effect file has no G3D resource.");
        VfxbNode table = models.Children.FirstOrDefault(c => c.Kind == "G3NT") ?? throw new InvalidDataException("The G3D resource has no model table.");
        int start = table.DataOffset ?? throw new InvalidDataException("The G3D model table has no data.");
        const int EntryBytes = 0x18;
        var ids = new List<uint>();
        for (int entry = 0; entry < (int)table.Size / EntryBytes; entry++)
            ids.Add(BitConverter.ToUInt32(_vfxb.Data, start + (entry * EntryBytes)));
        return (Slice(models), ids);
    }

    byte[] Slice(VfxbNode node) =>
        _vfxb.Data.AsSpan(node.DataOffset ?? throw new InvalidDataException($"The {node.Kind} node has no data."), (int)node.Size).ToArray();
}
