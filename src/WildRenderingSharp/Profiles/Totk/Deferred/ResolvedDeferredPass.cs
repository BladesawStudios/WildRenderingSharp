
namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// One deferred resolve pass, resolved to its compiled program and its <c>SystemModel.DeferredMain</c> material bytes; see <see
/// cref="DeferredResolvePass.ResolveDeferredPasses"/>.
/// </summary>
/// <param name="FieldLights">Whether the program is one of the game's <c>field_*</c> ones, which read the light pre-pass their own way.</param>
/// <param name="Tiled">Whether the program draws the screen as instanced tiles, as <c>field_hybrid</c>'s does.</param>
/// <param name="Source">The base name of the program's source, for <see cref="ResolveTrace"/>.</param>
public sealed record ResolvedDeferredPass(
    string Name, uint Program, uint MaterialBuffer, int PassIndex, bool FieldLights = false, bool Tiled = false, string Source = "");
