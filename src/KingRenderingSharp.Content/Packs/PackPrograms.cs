using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Prepared;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Packs;

/// <summary>The programs of prepared shader archives, as the text the renderer links: a store of <c>&lt;shading model&gt;_prog&lt;n&gt;_extracted.vert</c> and <c>.frag</c> files.</summary>
/// <remarks>
/// A program is found by the name of its shading model, in whichever archive holds a model of that name. When a program has both stages, a sampler both declare takes the fragment
/// stage's binding in the vertex stage too: a texture is bound once for a program, and a GL uniform of one name has one binding, while the game numbers each stage's texture slots on its own.
/// The archives must have been prepared with the reflected text, and with every pass the renderer draws, or a program is missing.
/// </remarks>
public sealed partial class PackPrograms(IReadOnlyDictionary<string, PreparedArchive> archives) : IContentFiles
{
    readonly ConcurrentDictionary<string, PreparedModel?> _models = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<(string Shading, int Program), (string? Vertex, string? Fragment)> _texts = new();

    [GeneratedRegex(@"^(?<shading>.+)_prog(?<program>\d+)_extracted\.(?<stage>vert|frag)$")]
    private static partial Regex ProgramFile();

    [GeneratedRegex(@"layout\s*\(\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s+(?<type>\w+)\s+(?<name>\w+)\s*;")]
    private static partial Regex SamplerDeclaration();

    /// <summary>The shading model of that name in any archive, or null.</summary>
    public PreparedModel? Model(string shading) => _models.GetOrAdd(shading, name =>
    {
        foreach (PreparedArchive archive in archives.Values)
            if (archive.Model(name) is { } model) return model;
        return null;
    });

    /// <summary>The cleaned-up text of a program's stages; a stage the program lacks is null.</summary>
    public (string? Vertex, string? Fragment) Text(string shading, int program) => _texts.GetOrAdd((shading, program), key =>
    {
        PreparedModel? model = Model(key.Shading);
        if (model is null) return (null, null);
        string? vertex = model.Glsl(key.Program, ShaderStage.Vertex), fragment = model.Glsl(key.Program, ShaderStage.Fragment);
        if (vertex is not null && fragment is not null) vertex = UnifySamplerBindings(vertex, fragment);
        return (vertex, fragment);
    });

    static string UnifySamplerBindings(string vertex, string fragment)
    {
        Dictionary<string, string> fragmentBindings = [];
        foreach (Match match in SamplerDeclaration().Matches(fragment)) fragmentBindings[match.Groups["name"].Value] = match.Groups["binding"].Value;
        return SamplerDeclaration().Replace(vertex, match => fragmentBindings.TryGetValue(match.Groups["name"].Value, out string? binding)
            ? $"layout (binding = {binding}) uniform {match.Groups["type"].Value} {match.Groups["name"].Value};"
            : match.Value);
    }

    static bool TryParse(string name, out string shading, out int program, out bool vertex)
    {
        Match match = ProgramFile().Match(name);
        (shading, program, vertex) = match.Success ? (match.Groups["shading"].Value, int.Parse(match.Groups["program"].Value), match.Groups["stage"].Value == "vert") : ("", -1, false);
        return match.Success;
    }

    public bool Exists(string name) => TryParse(name, out string shading, out int program, out bool vertex)
        && Text(shading, program) is var (v, f) && (vertex ? v : f) is not null;

    public byte[] ReadBytes(string name)
    {
        if (TryParse(name, out string shading, out int program, out bool vertex) && Text(shading, program) is var (v, f) && (vertex ? v : f) is { } text)
            return System.Text.Encoding.UTF8.GetBytes(text);
        throw new FileNotFoundException($"No archive holds the program {name}", name);
    }

    public IEnumerable<string> Enumerate(string pattern)
    {
        // A pattern of a shading model's name and any program, as the deferred passes are found: <head>*<tail> with the program number in the star.
        int star = pattern.IndexOf('*');
        if (star < 0) return Exists(pattern) ? [pattern] : [];
        string head = pattern[..star], tail = pattern[(star + 1)..];
        const string marker = "_prog";
        if (!head.EndsWith(marker, StringComparison.Ordinal)) return [];
        string shading = head[..^marker.Length];
        if (Model(shading) is not { } model) return [];
        List<string> found = [];
        for (int program = 0; program < model.ProgramCount; program++)
        {
            string name = $"{head}{program}{tail}";
            if (Exists(name)) found.Add(name);
        }

        return found;
    }

    public string Stamp(string name) => "pack";
}
