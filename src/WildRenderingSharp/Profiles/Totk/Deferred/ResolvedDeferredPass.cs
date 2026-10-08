
namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// One deferred resolve pass, resolved to its compiled program and its <c>SystemModel.DeferredMain</c> material bytes; see <see
/// cref="DeferredResolvePass.ResolveDeferredPasses"/>.
/// </summary>
/// <param name="Tiled">Whether the program draws the screen as instanced tiles, as <c>field_hybrid</c>'s does.</param>
public sealed record ResolvedDeferredPass(
    string Name, uint Program, uint MaterialBuffer, int PassIndex, bool FieldLights = false, bool Tiled = false, string Source = "");
