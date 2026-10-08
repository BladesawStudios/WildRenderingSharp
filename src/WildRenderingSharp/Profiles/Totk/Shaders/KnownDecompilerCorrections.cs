using System.Text.RegularExpressions;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// Patches one individually verified decompiler corruption. Deliberately not a general rule, and kept apart from <see
/// cref="GlslSanitizer"/>, whose header forbids editing shader logic.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeferredResolvePass</c>'s compose shader already documents this bug class in <c>material_prog10338</c>'s emission term: Ryujinx renders negation as "0.0 - x", and in some shaders that is
/// mis-associated into a stray "v * 0.0" term, leaving an expression negative for every input. That fix clamps the consumed value rather than the shader text. This class fixes a second, independently
/// verified instance of the same formula shape by the same philosophy, clamping the corrupted sub-expression to a sensible floor, in the one place a consumer-side clamp cannot reach.
/// </para>
/// <para>
/// The pattern <c>&lt;var&gt; * 0.0 - max(min(0.0 - &lt;var2&gt;, 25.0), 10.0) + -10.0</c> is present, identical apart from variable numbering, in 14 separately compiled "chara forward" programs (grep the
/// decompiled cache for <c>0.0 - max(min(0.0 - temp_N, 25.0), 10.0) + -10.0</c>), including <c>material_prog10338</c> itself. It evaluates strictly negative for any input: the dead <c>* 0.0</c>
/// erases what should be a real term, leaving <c>-(clamped height)-10</c>, always in [-35,-20], scaled to [-2.33,-1.33] before an outer +[0,1].
/// </para>
/// <para>
/// A shader-text patch is needed here where the emission fix needed none because emission passes through a G-buffer texture the compose shader reads back and can clamp. This term is consumed inside one
/// forward-program invocation, feeding output_color directly, so there is no boundary to intercept it at. No blend state, exposure scaling or post-composite floor can fix it from outside: a per-channel
/// floor against the deferred base (see <c>ForwardPass.FlipIntoWithFloor</c>) prevents a full black-out but cannot restore the relative proportion between channels once a term multiplying a different
/// material coefficient has crushed one disproportionately. Fixing the corrupted sub-expression is the only lever left.
/// </para>
/// </remarks>
public static class KnownDecompilerCorrections
{
    // Matches the sub-expression that should be the first argument of an outer fma(...), keyed on the structural shape (the literal 25.0/10.0/-10.0 constants), not on temp_N numbers, which renumber on every --prepare run. Captures the two variable names so they are preserved verbatim.
    static readonly Regex CorruptedHeightTerm = new(
        @"(temp_\d+) \* 0\.0 - max\(min\(0\.0 - (temp_\d+), 25\.0\), 10\.0\) \+ -10\.0",
        RegexOptions.Compiled);

    /// <summary>
    /// Wraps the confirmed always-negative sub-expression in <c>max(..., 0.0)</c> wherever it appears: floor a provably corrupted
    /// value at a sensible minimum rather than invent a replacement. The enclosing <c>fma(term, 0.0666667, temp_N)</c> then
    /// collapses to <c>temp_N</c>, always in [0,1], instead of a value that can only subtract.
    /// </summary>
    public static string Apply(string source) =>
        CorruptedHeightTerm.Replace(source, "max($1 * 0.0 - max(min(0.0 - $2, 25.0), 10.0) + -10.0, 0.0)");
}
