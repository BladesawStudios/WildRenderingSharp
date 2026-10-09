using System.Text.RegularExpressions;

namespace WildRenderingSharp.Debug;

/// <summary>
/// Instruments a decompiled fragment shader so one chosen intermediate (<c>temp_N</c>, in the decompiler's numbering) is shown as
/// the final pixel colour instead of the shader's output.
/// </summary>
internal static class ShaderStepDebugger
{
    const string UnreachedSentinel = "vec4(1.0, 0.5, 0.0, 1.0)";

    static readonly Regex DeclRegex = new(
        @"^\s*(?:precise\s+)?(bool|u?int|float|[iu]?vec[234])\s+(temp_\d+)\s*;\s*$",
        RegexOptions.Multiline);

    static readonly Regex AssignRegex = new(
        @"^(?<indent>[ \t]*)temp_(?<n>\d+)\s*=(?!=)",
        RegexOptions.Compiled);

    // Every real fragment output the shader declares (a G-buffer program has several: albedo, normal and emission at different
    // locations). The override must overwrite all of them, or the value shows only on whichever attachment comes first in the
    // file.
    static readonly Regex OutputDeclRegex = new(
        @"^\s*layout\s*\(location\s*=\s*\d+\)\s*out\s+vec4\s+(\w+(?:\[0\])?)\s*;",
        RegexOptions.Multiline);

    public static Dictionary<int, string> ParseTempTypes(string fragSource)
    {
        var types = new Dictionary<int, string>();
        foreach (Match m in DeclRegex.Matches(fragSource))
        {
            string type = m.Groups[1].Value;
            int n = int.Parse(m.Groups[2].Value["temp_".Length..]);
            types[n] = type;
        }
        return types;
    }

    public static string Instrument(string fragSource, out List<int> availableTargets, bool suppressDiscard = false)
    {
        var types = ParseTempTypes(fragSource);
        var targets = new SortedSet<int>();
        var lines = fragSource.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length + 64);

        foreach (string line in lines)
        {
            if (suppressDiscard && line.Trim() == "discard;")
            {
                output.Add(line.Replace("discard;", "/* discard suppressed for step debugging */;"));
                continue;
            }
            output.Add(line);
            var m = AssignRegex.Match(line);
            if (!m.Success)
                continue;
            int n = int.Parse(m.Groups["n"].Value);
            if (!types.TryGetValue(n, out string? glslType))
                continue; // no declaration found for this temp - shouldn't happen, skip defensively rather than emit invalid GLSL
            targets.Add(n);
            string indent = m.Groups["indent"].Value;
            // No "return" on purpose (see the class doc): a later capture of the same target on this path must be able to overwrite this one.
            output.Add($"{indent}if (uDebugStepTarget == {n}) {{ dbgCapturedValue = {CastToVec4($"temp_{n}", glslType)}; dbgCaptureHit = true; }}");
        }

        availableTargets = [.. targets];

        // The override replaces every real fragment output, not only the first found, or the value shows only on whichever attachment is textually first (e.g. not the Albedo view).
        var outputNames = OutputDeclRegex.Matches(string.Join('\n', output))
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        // The point where debug mode takes over every output. It must run before the shader's own final output write, not merely before main()'s closing brace: these files end with an unconditional
        // "return;" right before that brace, so inserting after it would be dead code. Insert before the last bare "return;" if there is one, else before the closing brace.
        int insertBefore = output.FindLastIndex(l => l.Trim() == "return;");
        if (insertBefore < 0)
            insertBefore = output.FindLastIndex(l => l.Trim() == "}");
        if (insertBefore >= 0 && outputNames.Count > 0)
        {
            string assignments = string.Join(' ', outputNames.Select(o => $"{o} = dbgCaptureHit ? dbgCapturedValue : {UnreachedSentinel};"));
            output.Insert(insertBefore, $"    if (uDebugStepTarget >= 0) {{ {assignments} return; }}");
        }

        string body = string.Join('\n', output);

        // "void main()" appears once (the entry point), so the debug uniform goes immediately before it and the two capture locals at the start of its body.
        body = body.Replace("void main()", "uniform int uDebugStepTarget;\nvoid main()");
        return body.Replace(
            "void main()\n{",
            "void main()\n{\n    vec4 dbgCapturedValue = vec4(0.0, 0.0, 0.0, 1.0);\n    bool dbgCaptureHit = false;");
    }

    static string CastToVec4(string name, string glslType) => glslType switch
    {
        "float" => $"vec4({name}, {name}, {name}, 1.0)",
        "vec2" => $"vec4({name}, 0.0, 1.0)",
        "vec3" => $"vec4({name}, 1.0)",
        "vec4" => name,
        "bool" => $"vec4({name} ? 1.0 : 0.0, {name} ? 1.0 : 0.0, {name} ? 1.0 : 0.0, 1.0)",
        "int" or "uint" => $"vec4(float({name}), float({name}), float({name}), 1.0)",
        "ivec2" or "uvec2" => $"vec4(vec2({name}), 0.0, 1.0)",
        "ivec3" or "uvec3" => $"vec4(vec3({name}), 1.0)",
        "ivec4" or "uvec4" => $"vec4({name})",
        // Unrecognised type (not expected, since both sides use the same regex): bright magenta so it reads as unsupported, never a silent wrong colour.
        _ => "vec4(1.0, 0.0, 1.0, 1.0)",
    };
}
