using System.Text.RegularExpressions;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// Patches ONE specific, individually-verified decompiler corruption - deliberately NOT a general
/// rule, and deliberately separate from <see cref="GlslSanitizer"/> (whose own header explicitly
/// forbids editing shader logic - this class exists precisely because that boundary matters and
/// shouldn't be blurred by adding logic-editing code into the same file that promises not to).
///
/// <c>DeferredResolvePass.cs</c>'s own <c>ComposeFragmentSource</c> remarks already document this
/// exact bug class in <c>material_prog10338</c>'s emission term: "Ryujinx renders negation as
/// '0.0 - x', and in some shaders that has been mis-associated into a stray 'v * 0.0' term,
/// leaving an expression that is negative for EVERY input." That fix clamps the SYMPTOM (the
/// consumed value) rather than the shader text. This class fixes a SECOND, independently-verified
/// instance of the exact same underlying formula shape - not by guessing what the "real" formula
/// should have been (unknowable without the original bytecode), but by applying the identical
/// "clamp the corrupted sub-expression to a physically sensible floor" philosophy, IN the one place
/// a downstream consumer-side clamp genuinely cannot reach it (see below).
///
/// THE PATTERN: <c>&lt;var&gt; * 0.0 - max(min(0.0 - &lt;var2&gt;, 25.0), 10.0) + -10.0</c> -
/// confirmed present, byte-for-byte identical apart from variable numbering, in 14 separately
/// compiled "chara forward" programs (grep the decompiled shader cache for the literal fingerprint
/// <c>0.0 - max(min(0.0 - temp_N, 25.0), 10.0) + -10.0</c>) - INCLUDING <c>material_prog10338</c>
/// itself, the same program the emission fix above targets. That is independent confirmation this
/// is the same systemic corruption recurring across a shared boilerplate formula, not a one-off.
/// Proven, algebraically and independent of every uniform/UBO value that can reach it (see
/// tasks_set1.md's full derivation), to evaluate strictly negative for any possible input: the
/// dead <c>* 0.0</c> erases what should be a real term, leaving <c>-(clamped height)-10</c>, always
/// in [-35,-20], scaled to [-2.33,-1.33] before an outer +[0,1] addition - never non-negative.
///
/// WHY A SHADER-TEXT PATCH, WHEN THE EMISSION FIX DID NOT NEED ONE: emission flows through an
/// intermediate G-buffer TEXTURE WildRenderingSharp's own compose shader reads back and can clamp externally.
/// This term is consumed entirely WITHIN one forward-program invocation, feeding output_color
/// directly - there is no intermediate boundary outside the shader to intercept it at. Confirmed
/// (this session) that no combination of blend state, exposure scaling, or post-composite flooring
/// can fix this from outside: a per-channel floor against the correct deferred base (see
/// ForwardPass.cs's FlipIntoWithFloor) prevents catastrophic full black-out but cannot restore the
/// RELATIVE proportion between channels once one channel's delta has been crushed disproportionately
/// more than the others by a term multiplying a different real per-channel material coefficient -
/// the floor operates per-channel and has no way to know channel A "should" have ended up larger
/// than channel B once both are already computed. Fixing the actual corrupted sub-expression is the
/// only remaining lever.
/// </summary>
public static class KnownDecompilerCorrections
{
    // Matches the sub-expression that should be the first argument of an outer fma(...) call -
    // deliberately keyed on the STRUCTURAL shape (the literal 25.0/10.0/-10.0 constants), not on
    // specific temp_N numbers, since those renumber on every ShaderLibrary.CompileTool --prepare
    // run (see CLAUDE.md). Captures the two variable names so they can be preserved verbatim.
    static readonly Regex CorruptedHeightTerm = new(
        @"(temp_\d+) \* 0\.0 - max\(min\(0\.0 - (temp_\d+), 25\.0\), 10\.0\) \+ -10\.0",
        RegexOptions.Compiled);

    /// <summary>
    /// Wraps the confirmed-always-negative sub-expression in <c>max(..., 0.0)</c> wherever it
    /// appears - the same "floor a provably-corrupted value at a physically sensible minimum,
    /// don't invent a replacement" philosophy as the emission fix. In the worst case this makes
    /// the term evaluate to 0 instead of a deep negative, so the enclosing
    /// <c>fma(term, 0.0666667, temp_N)</c> collapses to just <c>temp_N</c> - itself always in
    /// [0,1], a sane fallback - rather than a value that can only ever subtract.
    /// </summary>
    public static string Apply(string source) =>
        CorruptedHeightTerm.Replace(source, "max($1 * 0.0 - max(min(0.0 - $2, 25.0), 10.0) + -10.0, 0.0)");
}
