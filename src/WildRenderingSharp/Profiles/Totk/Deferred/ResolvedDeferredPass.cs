
namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>One deferred resolve pass, resolved to its compiled program and its <c>SystemModel.DeferredMain</c> material bytes; see <see cref="DeferredResolvePass.ResolveDeferredPasses"/>.</summary>
/// <param name="PassIndex">The pass's position in the list it was resolved from, which is the pass-ID mask's numbering. Not its position among resolved passes: a pass with no program is skipped, and every later pass was once matched to the next one's pixels.</param>
public sealed record ResolvedDeferredPass(string Name, uint Program, uint MaterialBuffer, int PassIndex);
