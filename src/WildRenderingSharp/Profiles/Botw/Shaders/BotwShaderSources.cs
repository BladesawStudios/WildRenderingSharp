using System.Text.RegularExpressions;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Botw.Shaders;

public sealed partial class BotwShaderSources : IShaderSources
{
    static readonly Dictionary<string, uint> BlockBindings = new(StringComparer.Ordinal)
    {
        ["_support_buffer"] = BotwBindings.Support,
        ["_Context"] = BotwBindings.Camera,
        ["_Mtx"] = BotwBindings.Bones,
        ["_Env"] = BotwBindings.Environment,
        ["_Mat"] = BotwBindings.Material,
        ["_SceneMat"] = BotwBindings.SceneMaterial,
    };

    [GeneratedRegex(@"binding = \d+(, std(?:140|430)\) (?:uniform|buffer) (_\w+))")]
    private static partial Regex Block();

    public string Clean(string source) => Block().Replace(GlslSanitizer.Clean(source), m =>
        BlockBindings.TryGetValue(m.Groups[2].Value, out uint binding) ? $"binding = {binding}{m.Groups[1].Value}" : m.Value);

    public string CorrectForwardFragment(string fragmentSource) => fragmentSource;

    public string? Instance(string cleanedVertexSource) => null;
}
