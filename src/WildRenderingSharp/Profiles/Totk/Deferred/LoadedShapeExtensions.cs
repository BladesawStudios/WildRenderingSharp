using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>The deferred pass that lights a shape, and the part a shape plays in a shrine's entrance.</summary>
/// <remarks>
/// A shrine's entrance names its part in its materials' <c>o_dungeon_entrance_pass</c>: 2 is the rock and what is drawn with it, which the game puts in its
/// DungeonEntrance/Shrine queue; 3 and 4 are the hall seen through the rock's doorway, opaque and see-through, which go in the Internal queues. Only the hall is lit
/// by the entrance's own pass, in the entrance's own colours with no sun: its G-buffer programs write that pass's material ID (6), where the rock's write the
/// field's (0), and the rock is lit as the ground around it is.
/// </remarks>
internal static class LoadedShapeExtensions
{
    public const string EntrancePass = "field_entrance";

    // The pass that resolves the shape; behave 102 and the hall a shrine's entrance shows get theirs here when the manifest holds none.
    public static string DeferredPass(this LoadedShape shape)
    {
        if (IsSeenThroughDoorway(shape))
            return EntrancePass;
        string pass = shape.Tags.GetValueOrDefault("deferred_pass", "");
        return pass.Length == 0 && shape.Tags.GetValueOrDefault("o_material_behave") == "102" ? "field_miasma" : pass;
    }

    // The rock, as against the hall seen through it.
    public static bool IsEntranceRock(this LoadedShape shape) => EntrancePart(shape) == 2;

    public static bool IsSeenThroughDoorway(this LoadedShape shape) => EntrancePart(shape) is 3 or 4;

    // The hall's copy of the entrance rock, which lies on the rock's own surface and leans out from it: it adds nothing to what the doorway shows, and from inside
    // the rock its dissolve fire stands over the hall as slabs.
    public static bool IsHallRockCopy(this LoadedShape shape) => IsSeenThroughDoorway(shape) && shape.Material == "Mt_Rock_ZonauShrineEntranceNature_A";

    // The panel the doorway's green fire burns on, which the game files with the rock's Shrine queue by its material's name, as it has no entrance part.
    public static bool IsDoorwayPanel(this LoadedShape shape) => shape.Material == "Mt_WarpHole_BackAura_00";

    static int EntrancePart(LoadedShape shape) =>
        shape.Tags.TryGetValue("option.o_dungeon_entrance_pass", out var value) && int.TryParse(value, out int part) ? part : 0;
}
