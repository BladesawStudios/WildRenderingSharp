using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Materials;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// One deferred resolve pass, resolved to its compiled program and its <c>SystemModel.DeferredMain</c> material bytes; see <see
/// cref="DeferredPassLoader.Load"/>.
/// </summary>
/// <param name="Tiled">Whether the program draws the screen as instanced tiles, as <c>field_hybrid</c>'s does.</param>
public sealed record ResolvedDeferredPass(
    string Name, uint Program, MaterialBlock Material, int PassIndex, bool FieldLights = false, bool Tiled = false, string Source = "");
