using System.Text;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Packs;

/// <summary>The game's grass programs, as the system pack of a cache holds them: for each kind of vegetation the decompiled vertex and fragment stage, and the default material block every program reads as its own.</summary>
/// <remarks>The programs are the game's, as the translator wrote them; a host that draws grass spells them for its own context. A stage is the GLSL, the constants its control block bakes in, the slots of the uniform blocks it names and the names of its samplers by the translator's hex name for their slot.</remarks>
public sealed class GrassPrograms
{
    /// <summary>The kinds of vegetation by name.</summary>
    public static readonly IReadOnlyList<string> Kinds =
        ["Blade", "Cross", "Cover", "Turf", "TurfCross", "FlowerPetal", "FlowerNormal", "FlowerLong", "Leaves", "Shell", "Chips", "Stone"];

    public static readonly IReadOnlyList<string> Stages = ["Vertex", "Fragment"];

    /// <summary>One stage of a kind's program.</summary>
    public sealed record Stage(string Glsl, IReadOnlyList<float> Constants, IReadOnlyDictionary<string, int> Blocks, IReadOnlyDictionary<string, string> Samplers);

    readonly IContentFiles _files;

    GrassPrograms(IContentFiles files) => _files = files;

    /// <summary>The default material block of the shading model, which the programs read as <c>Mat</c>: the alpha reference and the like.</summary>
    public byte[] Material => _files.Exists("GrassMaterial.bin") ? _files.ReadBytes("GrassMaterial.bin") : [];

    /// <summary>The programs of a cache whose system pack holds them; null when it has none (a game without grass, or a cache not prepared yet). Opening the pack for the life of the process is what the returned object does.</summary>
    public static GrassPrograms? Of(CacheLayout cache)
    {
        if (!File.Exists(cache.SystemPack)) return null;
        GrassPrograms programs = new(new SystemPack(cache.SystemPack, null).Store(SystemPack.GrassStore));
        return programs._files.Exists("BladeVertex.bin") ? programs : null;
    }

    /// <summary>A kind's stage, or null when the cache has none by that name.</summary>
    public Stage? Read(string kind, string stage)
    {
        string file = $"{kind}{stage}.bin";
        if (!_files.Exists(file)) return null;
        RecordReader reader = new(_files.ReadBytes(file));
        string glsl = Encoding.UTF8.GetString(reader.Bytes().Span);
        List<float> constants = reader.List(4, r => r.F32());
        Dictionary<string, int> blocks = reader.List(6, r => (r.String(), r.I32())).ToDictionary(b => b.Item1, b => b.Item2);
        Dictionary<string, string> samplers = reader.List(4, r => (r.String(), r.String())).ToDictionary(s => s.Item1, s => s.Item2);
        return new Stage(glsl, constants, blocks, samplers);
    }
}
