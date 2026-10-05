using System.Linq;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Debug;

/// <summary>
/// Instruments a real decompiled fragment shader so a single chosen intermediate value
/// (<c>temp_N</c>, in the decompiler's own numbering - the same numbers visible when reading the
/// file directly) can be visualised as the final pixel colour instead of the shader's real output.
/// This is a text-level splice, not a real GPU instruction stepper (there is no such thing for a
/// compiled fragment program on real hardware) - it works because these shaders are already plain,
/// readable GLSL text with one assignment per line, not opaque bytecode.
///
/// A <c>temp_N</c> can be written more than once along the SAME executed path, not just once per
/// if/else branch - e.g. "give it a default, then maybe overwrite it a few lines later" is a common
/// pattern in this decompiler's output. An early-return at the FIRST assignment site (the original
/// design) shows the wrong thing whenever a later write on the same path would have overwritten it
/// - you'd see the stale default, never the value that actually reaches the rest of the shader.
/// Fixed by making every hook a plain CAPTURE (no return): `if (target == N) { captured = <cast>;
/// hit = true; }`, at every assignment site, with no early exit. Since these run in the same
/// sequential order the real assignments do, a later capture on the executed path naturally
/// overwrites an earlier one - exactly mirroring how the real variable would end up. Only at the
/// very end of `main()` (right before the shader's own real final output write) does the debugger
/// actually divert output_color: to whatever was last captured, or - if the target's assignment
/// site(s) never executed on this pixel's path at all - a solid-orange sentinel, so "never wrote to
/// it" and "wrote a real value" can never be confused for each other.
/// </summary>
public static class ShaderStepDebugger
{
    const string UnreachedSentinel = "vec4(1.0, 0.5, 0.0, 1.0)";

    static readonly Regex DeclRegex = new(
        @"^\s*(?:precise\s+)?(bool|u?int|float|[iu]?vec[234])\s+(temp_\d+)\s*;\s*$",
        RegexOptions.Multiline);

    static readonly Regex AssignRegex = new(
        @"^(?<indent>[ \t]*)temp_(?<n>\d+)\s*=(?!=)",
        RegexOptions.Compiled);

    /// <summary>Every real fragment output this shader declares (there can be more than one on a G-buffer program - albedo/normal/emission are separate <c>out vec4</c>s at different locations) - the debug override has to overwrite ALL of them, not just the first, or stepping a value only shows up on whichever attachment happens to come first in the file.</summary>
    static readonly Regex OutputDeclRegex = new(
        @"^\s*layout\s*\(location\s*=\s*\d+\)\s*out\s+vec4\s+(\w+(?:\[0\])?)\s*;",
        RegexOptions.Multiline);

    /// <summary>Every <c>temp_N</c> local's declared GLSL type, keyed by N - found by scanning the whole file for bare declaration lines (an assignment line always has an <c>=</c> right after the name, so it never matches this).</summary>
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

    /// <summary>
    /// Returns the instrumented RAW source (still needs <c>GlslSanitizer.Clean</c> and compilation,
    /// same as any other decompiled shader - see <c>ShaderProgramCache.Load</c>) plus every
    /// <c>temp_N</c> that got at least one debug hook, sorted ascending, for a step-through UI to
    /// jump between.
    /// </summary>
    /// <param name="suppressDiscard">
    /// When true, every bare <c>discard;</c> statement is replaced with a no-op instead of actually
    /// discarding the fragment. Without this, a value computed right before a real discard can never
    /// be inspected for a pixel that WOULD have been cut - the shader exits before the debug-override
    /// block at the end of <c>main()</c> ever runs, so "why does this always discard" is exactly the
    /// one question the debugger couldn't answer until this existed. Leaves the real output alone
    /// (target -1) unaffected in every other way; only matters once you've picked a real target.
    /// </param>
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
            // No "return" here on purpose - see the class doc. A later capture of the SAME target
            // on this executed path must be able to overwrite this one, exactly like the real
            // temp_N reassignment it sits next to would.
            output.Add($"{indent}if (uDebugStepTarget == {n}) {{ dbgCapturedValue = {CastToVec4($"temp_{n}", glslType)}; dbgCaptureHit = true; }}");
        }

        availableTargets = [.. targets];

        // Every real fragment output this shader declares - a G-buffer program can have several
        // (albedo/normal/emission are separate `out vec4`s at different locations), and the debug
        // override has to replace ALL of them, not just the first one found, or the visualised value
        // only shows up on whichever G-buffer attachment happens to be textually first in the file
        // rather than the one a caller is actually looking at (e.g. the Albedo view).
        var outputNames = OutputDeclRegex.Matches(string.Join('\n', output))
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        // The single point where debug mode actually takes over every output - must run BEFORE the
        // real shader's own final output write, not merely before main()'s closing brace: these
        // files consistently end with their own unconditional "return;" right before that brace
        // (confirmed against real decompiled output), and inserting after it would put this in
        // genuinely dead code, never executing at all. Insert before the LAST bare "return;" if one
        // exists (the common case); fall back to before the closing brace only for a file that
        // instead just falls off the end of main().
        int insertBefore = output.FindLastIndex(l => l.Trim() == "return;");
        if (insertBefore < 0)
            insertBefore = output.FindLastIndex(l => l.Trim() == "}");
        if (insertBefore >= 0 && outputNames.Count > 0)
        {
            string assignments = string.Join(' ', outputNames.Select(o => $"{o} = dbgCaptureHit ? dbgCapturedValue : {UnreachedSentinel};"));
            output.Insert(insertBefore, $"    if (uDebugStepTarget >= 0) {{ {assignments} return; }}");
        }

        string body = string.Join('\n', output);

        // "void main()" appears exactly once in these files (the entry point) - safe to insert the
        // debug uniform declaration immediately before it, and the two capture locals as the very
        // first statements of its body, wherever both are in the source.
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
        // Unrecognised type (shouldn't happen given the same regex both sides use) - bright
        // magenta so it's obviously "unsupported", never a silent wrong colour.
        _ => "vec4(1.0, 0.0, 1.0, 1.0)",
    };
}
