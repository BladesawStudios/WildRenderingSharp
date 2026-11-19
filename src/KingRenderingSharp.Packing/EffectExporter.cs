using System.Text;
using System.Text.Json;
using BfresSharp;
using BfresSharp.Gx2;
using BfresSharp.Model;
using BfresSharp.Model.Geometry;
using BfShaderSharp.Assets;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Pack;
using BymlSharp;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Games.Totk.Effects;
using PtclSharp;
using PtclSharp.Layout;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the effects the renderer draws from the effect file a pack holds: the volume mask's two emitters (program, static block, mesh, plugin, texture and manifest) and the field haze manifest.</summary>
/// <remarks>
/// The file is the game's <c>static</c> effect file, stored decoded by BfShaderSharp's effect importer, with the textures the emitters use. The VFXB tree is read with PtclSharp.
/// What each emitter yields is what the older preparer wrote, file for file.
/// </remarks>
internal static class EffectExporter
{
    const string File_ = "static";

    sealed record Emitter(string Set, string Name, string Id, bool Textured = false);

    static readonly Emitter[] VolumeMask =
    [
        new("Env_VolumeMask_Fog_MainField", "Haze_Far_simple_new", ParticleEmitterManifest.VolumeMaskId),
        new("Env_VolumeMask_AddFog_MainField", "Haze_Far", ParticleEmitterManifest.AddFogId, Textured: true),
    ];

    static readonly string[] HazeSets = ["Env_FieldHaze_Interaction_Lv01", "FieldHaze_Lv02", "FieldHaze_Lv03", "FieldHaze_Lv04", "FieldHaze_Lv05", "FieldHaze_Lv06", "FieldHaze_Lv07"];

    // How the field haze's opacity is averaged: how finely over the sprite's life, over how many pairs of texels, and from which seed.
    const int KeyedMode = 2, LifeSamples = 64, TexelPairs = 1 << 15, TexelSeed = 0x48A2E;

    /// <summary>The names of the textures the exported emitters read, from the effect file as the romfs has it; empty when it has none.</summary>
    public static IReadOnlyList<string> TextureNames(RomfsFiles romfs)
    {
        if (EffectImporter.ReadFile(romfs, File_) is not { } bytes) return [];
        EffectFile file = EffectFile.Open(bytes);
        List<string> names = [];
        foreach (Emitter emitter in VolumeMask.Where(e => e.Textured)) Add(names, file, Fields(file, emitter.Set, emitter.Name), "tex_slot0_guid");
        foreach (string set in HazeSets)
        {
            StructView fields = Fields(file, set, file.EmitterNames(set).First());
            Add(names, file, fields, "tex_slot1_guid");
            Add(names, file, fields, "tex_slot2_guid");
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    static void Add(List<string> names, EffectFile file, StructView fields, string slot)
    {
        if (fields.GetUInt64(slot) is var guid and not ulong.MaxValue) names.Add(file.TextureName((uint)guid));
    }

    static StructView Fields(EffectFile file, string set, string emitter) => new(PtclLayouts.For(PtclVersion.TotK_NintendoWareVfx2_15_3_1).Emitter, file.EmitterData(set, emitter));

    /// <summary>Returns the number of emitters and the haze levels written.</summary>
    public static (int Emitters, int HazeLevels) Export(AssetPack pack, CacheLayout layout, Action<string> log)
    {
        if (pack.GameData(EffectImporter.FileEntry(File_)) is not { } bytes) return (0, 0);
        EffectFile file = EffectFile.Open(bytes);
        Directory.CreateDirectory(layout.Effects);
        foreach (Emitter emitter in VolumeMask) ExportEmitter(pack, file, emitter, layout);
        int levels = ExportHaze(pack, file, layout);
        return (VolumeMask.Length, levels);
    }

    static void ExportEmitter(AssetPack pack, EffectFile file, Emitter emitter, CacheLayout layout)
    {
        byte[] data = file.EmitterData(emitter.Set, emitter.Name);
        StructView fields = new(PtclLayouts.For(PtclVersion.TotK_NintendoWareVfx2_15_3_1).Emitter, data);
        ApplyConstants(fields);

        (byte[] resource, IReadOnlyList<uint> modelIds) = file.Models();
        uint model = (uint)fields.GetUInt64("g3d_primitive_idx");
        int modelIndex = modelIds.ToList().IndexOf(model);
        if (modelIndex < 0) throw new InvalidDataException($"Emitter '{emitter.Name}' uses model {model:X8}, which its effect file does not hold.");
        Mesh mesh = Mesh.Read(resource, modelIndex);

        mesh.Write(Path.Combine(layout.Effects, ParticleEmitterManifest.MeshFile(emitter.Id)));
        System.IO.File.WriteAllBytes(Path.Combine(layout.Effects, ParticleEmitterManifest.StaticBlockFile(emitter.Id)), data[..ParticleEmitterManifest.StaticBlockBytes]);
        if (file.Plugin(emitter.Set, emitter.Name, "EP04", ParticleEmitterManifest.AreaLoopBytes) is { } areaLoop)
            System.IO.File.WriteAllBytes(Path.Combine(layout.Effects, ParticleEmitterManifest.PluginFile(emitter.Id)), areaLoop);
        if (fields.GetUInt64("tex_slot0_guid") is var texture and not ulong.MaxValue)
            System.IO.File.WriteAllBytes(Path.Combine(layout.Effects, ParticleEmitterManifest.TextureFile(emitter.Id)), pack.GameData(EffectImporter.TextureEntry(file.TextureName((uint)texture))) ?? throw new InvalidDataException($"The pack holds no texture for emitter '{emitter.Name}'."));
        System.IO.File.WriteAllBytes(Path.Combine(layout.Effects, ParticleEmitterManifest.ManifestFile(emitter.Id)), Manifest(fields, mesh).ToBytes());
        WriteProgram(file.ShaderArchive(), fields.GetInt32("shader_idx_normal"), ParticleEmitterManifest.ProgramName(emitter.Id), layout.Shaders);
    }

    static ParticleEmitterManifest Manifest(StructView fields, Mesh mesh) => new(
        (int)fields.GetUInt32("particle_lifespan"),
        Vector(fields, "particle_scale_xyz"), Vector(fields, "emitter_trans_xyz"), Vector(fields, "emitter_scale_xyz"),
        [.. Vector(fields, "emitter_color0_rgb"), fields.GetSingle("emitter_color0_alpha")],
        fields.GetByte("blend_mode_index"), fields.GetByte("cull_mode_index"), mesh.VertexCount, mesh.Indices.Length, mesh.TriangleStrip,
        Vector(fields, "shape_radius_xyz"),
        // One emission every interval plus one frames, each of the emit rate's particles, and each lives its lifespan.
        fields.GetUInt32("particle_lifespan") * fields.GetSingle("emit_rate") / (fields.GetInt32("emit_interval") + 1f));

    static float[] Vector(StructView fields, string name) => [fields.GetSingle(name, 0), fields.GetSingle(name, 1), fields.GetSingle(name, 2)];

    // The vertex and fragment stages of the program at an index of the effect's shader archive, as reflection-named GLSL; the fragment stage goes last, which is what marks the program as exported.
    static void WriteProgram(byte[] archive, int index, string name, string shaders)
    {
        BnshFile bnsh = BnshFile.Read(archive);
        if ((uint)index >= (uint)bnsh.VariationCount) throw new InvalidDataException($"The shader archive has {bnsh.VariationCount} programs, not {index + 1}.");
        ShaderProgramBinary program = bnsh.Variation(index).Program;
        Directory.CreateDirectory(shaders);
        foreach ((string extension, ShaderStage stage) in new[] { ("vert", ShaderStage.Vertex), ("frag", ShaderStage.Fragment) })
        {
            ShaderStageCode code = program.Code(stage) ?? throw new InvalidDataException($"The program has no code for {name}.{extension}.");
            System.IO.File.WriteAllText(Path.Combine(shaders, $"{name}.{extension}"), ReflectedGlsl.Extract(code, program.Reflection(stage)));
        }
    }

    // A track in constant mode keeps its value in a separate field, and the engine copies it into the first key of the track's table when the emitter is created.
    static void ApplyConstants(StructView fields)
    {
        foreach ((string mode, string constant, string keys, int components) in new[] { ("color0_mode", "color0_const_rgb", "kf_color0", 3), ("alpha0_mode", "alpha0_const", "kf_alpha0", 1), ("color1_mode", "color1_const_rgb", "kf_color1", 3), ("alpha1_mode", "alpha1_const", "kf_alpha1", 1) })
        {
            if (fields.GetByte(mode) != 0) continue;
            for (int component = 0; component < components; component++) fields.SetSingle(keys, fields.GetSingle(constant, component), component);
        }
    }

    static int ExportHaze(AssetPack pack, EffectFile file, CacheLayout layout)
    {
        (byte[] resource, IReadOnlyList<uint> modelIds) = file.Models();
        List<FieldHazeLevel> levels = [];
        float[] color = [];
        foreach (string set in HazeSets)
        {
            StructView fields = Fields(file, set, file.EmitterNames(set).First());
            ApplyConstants(fields);
            levels.Add(Level(pack, file, fields, Mesh.Read(resource, modelIds.ToList().IndexOf((uint)fields.GetUInt64("g3d_primitive_idx")))));
            color = [fields.GetSingle("color0_const_rgb", 0), fields.GetSingle("color0_const_rgb", 1), fields.GetSingle("color0_const_rgb", 2)];
        }

        System.IO.File.WriteAllBytes(Path.Combine(layout.Effects, FieldHazeManifest.FileName), (new FieldHazeManifest(color, [.. levels])).ToBytes());
        return levels.Count;
    }

    static FieldHazeLevel Level(AssetPack pack, EffectFile file, StructView fields, Mesh mesh)
    {
        // One sprite is emitted every interval plus one frames and lives its lifespan.
        float particles = fields.GetUInt32("particle_lifespan") / (fields.GetInt32("emit_interval") + 1f);
        float radius = fields.GetSingle("shape_radius_xyz", 0);
        float area = mesh.Extent(0) * fields.GetSingle("particle_scale_xyz", 0) * mesh.Extent(1) * fields.GetSingle("particle_scale_xyz", 1);

        // The fade in over the camera distances of 0x8A0 to 0x8A4, and out over 0x8A8 to 0x8AC where that begins after the fade in is over; the programs
        // of the outermost level have no fade out, and leave the field holding another number.
        float inStart = fields.GetSingle("unverified_8A0"), inEnd = fields.GetSingle("unverified_8A4");
        float outStart = fields.GetSingle("unverified_8A8"), outEnd = fields.GetSingle("unverified_8AC");
        bool fadesOut = outStart >= inEnd && outEnd > outStart;
        return new FieldHazeLevel(fields.GetSingle("emitter_trans_xyz", 2), radius, particles, area, Opacity(pack, file, fields),
            inStart, inEnd, fadesOut ? outStart : 0f, fadesOut ? outEnd : 0f, fields.GetSingle("unverified_8C4"));
    }

    // A sprite's opacity is clamp(4 * (the product of its two textures' alphas - the first track's value)) times the second track, and the engine scales
    // each by a custom value it sets, which is taken as one. The mean over the sprite's life and its texels is the track's mean times the texture term.
    static float Opacity(AssetPack pack, EffectFile file, StructView fields)
    {
        float threshold = fields.GetSingle("kf_alpha0", 0);
        float[]? first = Texels(pack, file, fields.GetUInt64("tex_slot1_guid"));
        float[]? second = Texels(pack, file, fields.GetUInt64("tex_slot2_guid"));
        Random random = new(TexelSeed);
        float coverage = 0f;
        for (int pair = 0; pair < TexelPairs; pair++)
        {
            float a = first is null ? 1f : first[random.Next(first.Length)];
            float b = second is null ? 1f : second[random.Next(second.Length)];
            coverage += Math.Clamp(4f * ((a * b) - threshold), 0f, 1f);
        }

        float life = 0f;
        for (int sample = 0; sample < LifeSamples; sample++) life += Track(fields, "alpha1", (sample + 0.5f) / LifeSamples);
        return coverage / TexelPairs * (life / LifeSamples);
    }

    // The alpha of each texel of the emitter's texture, 0 to 1, from the decoded RGBA8 the pack holds (width and height first).
    static float[]? Texels(AssetPack pack, EffectFile file, ulong guid)
    {
        if (guid == ulong.MaxValue) return null;
        byte[] stored = pack.GameData(EffectImporter.TextureEntry(file.TextureName((uint)guid))) ?? throw new InvalidDataException("The pack holds no texture for a field haze emitter.");
        float[] alphas = new float[(stored.Length - 8) / 4];
        for (int texel = 0; texel < alphas.Length; texel++) alphas[texel] = stored[8 + (texel * 4) + 3] / 255f;
        return alphas;
    }

    static float Track(StructView fields, string track, float time)
    {
        string keys = "kf_" + track;
        int count = (int)fields.GetUInt32(track + "_key_count");
        if (fields.GetByte(track + "_mode") != KeyedMode && count <= 1) return fields.GetSingle(track + "_const");

        float[] values = new float[count], times = new float[count];
        for (int key = 0; key < count; key++)
        {
            values[key] = fields.GetSingle(keys, key * 4);
            times[key] = fields.GetSingle(keys, (key * 4) + 3);
        }

        if (time <= times[0]) return values[0];
        for (int key = 1; key < count; key++)
            if (time <= times[key]) return values[key - 1] + ((values[key] - values[key - 1]) * (time - times[key - 1]) / MathF.Max(times[key] - times[key - 1], 1e-6f));
        return values[count - 1];
    }

    /// <summary>A particle mesh as the live half reads it: a position, a vertex colour and a texture coordinate per vertex, and the indices.</summary>
    sealed record Mesh(float[] Positions, float[] Colors, float[] Uvs, uint[] Indices, bool TriangleStrip)
    {
        public int VertexCount => Positions.Length / 3;

        public float Extent(int axis)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (int vertex = axis; vertex < Positions.Length; vertex += 3)
            {
                low = MathF.Min(low, Positions[vertex]);
                high = MathF.Max(high, Positions[vertex]);
            }

            return high - low;
        }

        // The model at the given place in the effect file's G3D resource.
        public static Mesh Read(byte[] resource, int modelIndex)
        {
            ResFile file = BfresFile.Read(resource);
            ResModel model = file.Models.Values.ElementAt(modelIndex);
            Shape shape = model.Shapes.Values.First();
            VertexBuffer buffer = model.VertexBuffers[shape.VertexBufferIndex];
            VertexAttrib position = buffer.Attributes.Values.FirstOrDefault(a => a.Name == "_p0") ?? throw new InvalidDataException($"Model '{model.Name}' has no position attribute.");
            VertexAttrib? color = buffer.Attributes.Values.FirstOrDefault(a => a.Name == "_c0"), uv = buffer.Attributes.Values.FirstOrDefault(a => a.Name == "_u0");
            int count = (int)buffer.VertexCount;
            float[] positions = new float[count * 3], colors = new float[count * 4], uvs = new float[count * 2];
            for (int v = 0; v < count; v++)
            {
                float[] p = Decode(buffer, position, v);
                positions[v * 3] = p[0];
                positions[(v * 3) + 1] = p[1];
                positions[(v * 3) + 2] = p.Length > 2 ? p[2] : 0f;
                // A model without vertex colours is opaque white.
                float[]? c = color is null ? null : Decode(buffer, color, v);
                for (int i = 0; i < 4; i++) colors[(v * 4) + i] = c is not null && i < c.Length ? c[i] : 1f;
                float[]? t = uv is null ? null : Decode(buffer, uv, v);
                uvs[v * 2] = t?[0] ?? 0f;
                uvs[(v * 2) + 1] = t is { Length: > 1 } ? t[1] : 0f;
            }

            BfresSharp.Model.Geometry.Mesh mesh = shape.Meshes[0];
            return new Mesh(positions, colors, uvs, ReadIndices(mesh), mesh.PrimitiveType.ToString().Contains("Strip", StringComparison.Ordinal));
        }

        static uint[] ReadIndices(BfresSharp.Model.Geometry.Mesh mesh)
        {
            ReadOnlySpan<byte> data = mesh.IndexData.Span;
            int size = mesh.IndexSize;
            uint[] indices = new uint[data.Length / size];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = size == 2 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]) : size == 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]) : data[i];
            return [.. indices.Select(i => i + mesh.FirstVertex)];
        }

        // One vertex's attribute as floats, for the formats a particle mesh has.
        static float[] Decode(VertexBuffer buffer, VertexAttrib attribute, int vertex)
        {
            VertexStream stream = buffer.Streams[attribute.BufferIndex];
            ReadOnlySpan<byte> at = stream.Data.Span[(vertex * stream.Stride + attribute.Offset)..];
            // The format's name says how many components it has, how wide each is and how it reads: Format_16_16_UNorm, Format_8_8_8_8_UIntToSingle, Format_32_32_Single.
            string[] parts = attribute.Format.ToString().Split('_');
            List<int> widths = [];
            int at2 = 1;
            while (at2 < parts.Length && int.TryParse(parts[at2], out int width)) { widths.Add(width); at2++; }
            string kind = string.Join("_", parts[at2..]);
            if (widths.Count == 0 || widths.Any(w => w != widths[0]) || widths[0] is not (8 or 16 or 32)) throw new NotSupportedException($"Particle mesh attribute {attribute.Name} has the format {attribute.Format}.");
            float[] values = new float[widths.Count];
            for (int i = 0; i < values.Length; i++)
            {
                int size = widths[0] / 8;
                ReadOnlySpan<byte> item = at[(i * size)..];
                values[i] = (widths[0], kind) switch
                {
                    (32, "Single") => BitConverter.ToSingle(item),
                    (16, "Single") => (float)BitConverter.ToHalf(item),
                    (8, "UNorm") => item[0] / 255f,
                    (16, "UNorm") => BitConverter.ToUInt16(item) / 65535f,
                    (8, "SNorm") => Math.Max((sbyte)item[0] / 127f, -1f),
                    (16, "SNorm") => Math.Max(BitConverter.ToInt16(item) / 32767f, -1f),
                    (8, "UIntToSingle" or "UInt") => item[0],
                    (16, "UIntToSingle" or "UInt") => BitConverter.ToUInt16(item),
                    (8, "SIntToSingle" or "SInt") => (sbyte)item[0],
                    (16, "SIntToSingle" or "SInt") => BitConverter.ToInt16(item),
                    _ => throw new NotSupportedException($"Particle mesh attribute {attribute.Name} has the format {attribute.Format}."),
                };
            }

            return values;
        }

        static float[] Floats(ReadOnlySpan<byte> at, int count)
        {
            float[] values = new float[count];
            for (int i = 0; i < count; i++) values[i] = BitConverter.ToSingle(at[(i * 4)..]);
            return values;
        }

        static float[] Halves(ReadOnlySpan<byte> at, int count)
        {
            float[] values = new float[count];
            for (int i = 0; i < count; i++) values[i] = (float)BitConverter.ToHalf(at[(i * 2)..]);
            return values;
        }

        public void Write(string path)
        {
            using FileStream stream = System.IO.File.Create(path);
            using BinaryWriter writer = new(stream);
            foreach (float value in Positions) writer.Write(value);
            foreach (float value in Colors) writer.Write(value);
            foreach (float value in Uvs) writer.Write(value);
            foreach (uint index in Indices) writer.Write(index);
        }
    }

    /// <summary>An effect file read down to its VFXB tree, with the pieces of it an exported emitter needs.</summary>
    sealed class EffectFile
    {
        readonly VfxbFile _vfxb;
        readonly Dictionary<uint, string> _textures;

        EffectFile(VfxbFile vfxb, Dictionary<uint, string> textures)
        {
            _vfxb = vfxb;
            _textures = textures;
        }

        public static EffectFile Open(byte[] decoded)
        {
            Byml root = Byml.FromBinary(decoded);
            byte[] binary = root.AsMap.TryGetValue("PtclBin", out Byml? bin) ? bin.Binary : throw new InvalidDataException("The effect file has no PtclBin entry.");
            Dictionary<uint, string> textures = [];
            if (root.AsMap.TryGetValue("Textures", out Byml? list) && list.IsArray)
                foreach (Byml texture in list.AsArray) textures[texture.AsMap["guid"].UInt] = texture.AsMap["name"].String;
            return new EffectFile(VfxbReader.Read(binary, VfxbLayouts.TotK), textures);
        }

        // The name of a texture the file's emitters name by id, which is the name of its file in TexToGo.
        public string TextureName(uint guid) => _textures.TryGetValue(guid, out string? name) ? name : throw new InvalidDataException($"The effect file lists no texture {guid:X8}.");

        // The emitter's data block, which every field of its layout is read from.
        public byte[] EmitterData(string setName, string emitterName)
        {
            VfxbEmitterSet set = _vfxb.EmitterSets.FirstOrDefault(s => s.Name == setName) ?? throw new InvalidDataException($"The effect file has no emitter set '{setName}'.");
            VfxbEmitter emitter = set.Emitters.FirstOrDefault(e => e.Name == emitterName) ?? throw new InvalidDataException($"Emitter set '{setName}' has no emitter '{emitterName}'.");
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
            return Slice(shaders.Children.FirstOrDefault(c => c.Kind == "GRSR") ?? throw new InvalidDataException("The shader node has no graphics shader file."));
        }

        // The G3D resource (a BFRES file) holding every model the file's emitters use, and the model ids in its order.
        public (byte[] Resource, IReadOnlyList<uint> Ids) Models()
        {
            VfxbNode models = _vfxb.Roots.FirstOrDefault(r => r.Kind == "G3PR") ?? throw new InvalidDataException("The effect file has no G3D resource.");
            VfxbNode table = models.Children.FirstOrDefault(c => c.Kind == "G3NT") ?? throw new InvalidDataException("The G3D resource has no model table.");
            int start = table.DataOffset ?? throw new InvalidDataException("The G3D model table has no data.");
            const int EntryBytes = 0x18;
            List<uint> ids = [];
            for (int entry = 0; entry < (int)table.Size / EntryBytes; entry++) ids.Add(BitConverter.ToUInt32(_vfxb.Data, start + (entry * EntryBytes)));
            return (Slice(models), ids);
        }

        byte[] Slice(VfxbNode node) => _vfxb.Data.AsSpan(node.DataOffset ?? throw new InvalidDataException($"The {node.Kind} node has no data."), (int)node.Size).ToArray();
    }
}
