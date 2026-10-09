using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

internal static class LoadedShapeExtensions
{
    /// <summary>The pass that resolves the shape; behave 102 and shrine entrances get theirs here when the manifest holds none.</summary>
    public static string DeferredPass(this LoadedShape shape)
    {
        if (IsDungeonEntrance(shape))
            return "field_entrance";
        string pass = shape.Tags.GetValueOrDefault("deferred_pass", "");
        return pass.Length == 0 && shape.Tags.GetValueOrDefault("o_material_behave") == "102" ? "field_miasma" : pass;
    }

    static bool IsDungeonEntrance(LoadedShape shape) =>
        shape.Tags.TryGetValue("option.o_dungeon_entrance_pass", out var value) && value is not ("0" or "<Default Value>");
}
