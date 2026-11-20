using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Games.Totk.Effects;

/// <summary>
/// What preparation recorded about one particle emitter: the values the live half needs to draw it that are not in its static block or its mesh. It is
/// the contract between the importer, which writes it, and <see cref="ParticleEmitterAsset"/>, which reads it.
/// </summary>
/// <remarks>
/// An emitter's files sit in the effects store under its id: this manifest, the static block (the emitter's first <see cref="StaticBlockBytes"/>
/// bytes, which its shaders read directly), and the mesh. Its decompiled program is in the shaders store as <see cref="ProgramName"/>.
/// </remarks>
public sealed record ParticleEmitterManifest(
    int Life, float[] ParticleScale, float[] Translation, float[] Scale, float[] Color0,
    int BlendMode, int CullMode, int VertexCount, int IndexCount, bool TriangleStrip, float[] ShapeRadius, float ParticlesAlive)
{
    // The size of the emitter data its shaders read as a uniform block.
    public const int StaticBlockBytes = 0xCA0;

    // The emitter the game fills its volume mask with.
    public const string VolumeMaskId = "volume_mask";

    // The emitter that adds to the volume mask with sprites scattered around the camera.
    public const string AddFogId = "volume_mask_add_fog";

    public static string ProgramName(string id) => $"effect_{id}";

    // The names of an emitter's files in the effects store.
    public static string ManifestFile(string id) => id + ".manifest.bin";

    public static string StaticBlockFile(string id) => id + ".static.bin";

    // The emitter's first texture, when it has one: the size as two integers, then RGBA8.
    public static string TextureFile(string id) => id + ".texture.bin";

    // The payload of the emitter's area loop plugin, when it has one, which wraps its particles into a box around the camera.
    public static string PluginFile(string id) => id + ".plugin.bin";

    // The size of that payload.
    public const int AreaLoopBytes = 0x4C;

    public static string MeshFile(string id) => id + ".mesh.bin";

    // Whether the emitter was prepared: its program is the file that is written last.
    public static bool IsPrepared(IContentFiles shaders, IContentFiles effects, string id) =>
        shaders.Exists(ProgramName(id) + ".frag") && effects.Exists(ManifestFile(id));

    // The manifest as bytes: the numbers in the order of the fields, an array as its length then its values.
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            w.Write(Life);
            foreach (float[] array in new[] { ParticleScale, Translation, Scale, Color0 })
                WriteFloats(w, array);
            w.Write(BlendMode);
            w.Write(CullMode);
            w.Write(VertexCount);
            w.Write(IndexCount);
            w.Write(TriangleStrip);
            WriteFloats(w, ShapeRadius);
            w.Write(ParticlesAlive);
        }

        return stream.ToArray();
    }

    public static ParticleEmitterManifest FromBytes(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        int life = r.ReadInt32();
        float[] particleScale = ReadFloats(r), translation = ReadFloats(r), scale = ReadFloats(r), color0 = ReadFloats(r);
        int blend = r.ReadInt32(), cull = r.ReadInt32(), vertices = r.ReadInt32(), indices = r.ReadInt32();
        bool strip = r.ReadBoolean();
        float[] shapeRadius = ReadFloats(r);
        return new ParticleEmitterManifest(life, particleScale, translation, scale, color0, blend, cull, vertices, indices, strip, shapeRadius, r.ReadSingle());
    }

    internal static void WriteFloats(BinaryWriter w, float[] values)
    {
        w.Write(values.Length);
        foreach (float v in values) w.Write(v);
    }

    internal static float[] ReadFloats(BinaryReader r)
    {
        var values = new float[r.ReadInt32()];
        for (int i = 0; i < values.Length; i++) values[i] = r.ReadSingle();
        return values;
    }
}
