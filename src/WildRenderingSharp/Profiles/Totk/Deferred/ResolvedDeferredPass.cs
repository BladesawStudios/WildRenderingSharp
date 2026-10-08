
namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// One deferred resolve pass, resolved to its compiled program and its <c>SystemModel.DeferredMain</c> material bytes; see <see
/// cref="DeferredResolvePass.ResolveDeferredPasses"/>.
/// </summary>
public sealed record ResolvedDeferredPass(string Name, uint Program, uint MaterialBuffer, int PassIndex);
