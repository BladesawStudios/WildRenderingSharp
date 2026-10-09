using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>Fills the per-actor blocks TotK's shaders read: the bone palette and the model transform.</summary>
static class TotkActorUniforms
{
    // The blocks instanced actors leave zeroed, since each instance carries its own transform.
    public static IReadOnlyList<UboSpec> InstancedBlocks { get; } = [TotkBlocks.Bones, TotkBlocks.ShapeMatrix];

    public static Ubo[] Build(in SkinningData actor) => [BonePalette.For(TotkBlocks.Bones, actor), ShapeMatrix(actor.PlacementRows)];

    internal static Ubo ShapeMatrix(ReadOnlySpan<System.Numerics.Vector4> placementRows)
    {
        var block = new UboWriter(TotkBlocks.ShapeMatrix);
        block.Set(TotkBlocks.ShapeModel, placementRows);
        return block.ToUbo(FrameUniformKeys.ShapeMatrix);
    }
}
