namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>
/// What preparation recorded about one particle emitter: the values the live half needs to draw it that are not in its static block or its mesh. It is
/// the contract between the exporter, which writes it, and <see cref="ParticleEmitterAsset"/>, which reads it.
/// </summary>
/// <remarks>
/// An emitter's files sit in the effects folder under its id: this manifest, the static block (the emitter's first <see cref="StaticBlockBytes"/>
/// bytes, which its shaders read directly), and the mesh. Its decompiled program is in the shaders folder as <see cref="ProgramName"/>.
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

    public static string ManifestPath(string effectsDirectory, string id) => Path.Combine(effectsDirectory, id + ".json");

    public static string StaticBlockPath(string effectsDirectory, string id) => Path.Combine(effectsDirectory, id + ".static.bin");

    // The emitter's first texture, when it has one: the size as two integers, then RGBA8.
    public static string TexturePath(string effectsDirectory, string id) => Path.Combine(effectsDirectory, id + ".texture.bin");

    // The payload of the emitter's area loop plugin, when it has one, which wraps its particles into a box around the camera.
    public static string PluginPath(string effectsDirectory, string id) => Path.Combine(effectsDirectory, id + ".plugin.bin");

    // The size of that payload.
    public const int AreaLoopBytes = 0x4C;

    public static string MeshPath(string effectsDirectory, string id) => Path.Combine(effectsDirectory, id + ".mesh.bin");

    // Whether the emitter was prepared: its program is the file that is written last.
    public static bool IsPrepared(string shadersDirectory, string effectsDirectory, string id) =>
        File.Exists(Path.Combine(shadersDirectory, ProgramName(id) + ".frag")) && File.Exists(ManifestPath(effectsDirectory, id));
}
