using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

static class LoadedShapeExtensions
{
    /// <summary>The <c>SystemModel.DeferredMain</c> resolve pass the shape's material goes through, or empty if the manifest names none.</summary>
    public static string DeferredPass(this LoadedShape shape) =>
        shape.Tags.TryGetValue("deferred_pass", out var pass) ? pass : "";
}
