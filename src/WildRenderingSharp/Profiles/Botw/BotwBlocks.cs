using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Botw;

/// <summary>The uniform blocks BotW's shaders read. The camera, light and scene-material slots shared with TotK are in the <c>Gsys</c> classes.</summary>
static class BotwBlocks
{
    public static readonly UboSpec Context = new("gsys_context", BotwBindings.Camera, 2320);
    public static readonly UboMatrix CascadeMatrix = new(42, 4);
    public const int CascadeSplits = 58;

    public static readonly UboSpec Environment = new("gsys_environment", BotwBindings.Environment, 992);
    public static readonly UboMatrix ViewToWorld = new(43, 3);
    public const int CascadeTexel = 53;

    // Fog and curve slots the pre-shading passes read as exponents; zero would make their pow() NaN.
    public static readonly (int Slot, int Component)[] ExponentSlots = [(18, 1), (27, 0), (27, 1), (29, 0), (29, 2)];

    public static readonly UboSpec SceneMaterial = new("gsys_scene_material", BotwBindings.SceneMaterial, 96);
    public const int ProcDiscardScales = 68;

    public static readonly UboSpec Bones = BonePalette.SpecAt(BotwBindings.Bones);
}
