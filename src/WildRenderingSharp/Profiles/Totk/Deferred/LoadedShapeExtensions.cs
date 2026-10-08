using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

static class LoadedShapeExtensions
{
    public static string DeferredPass(this LoadedShape shape) =>
        shape.Tags.TryGetValue("deferred_pass", out var pass) ? pass : "";
}
