using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkActorUniforms
{
    public static UniformBlock[] Build(in SkinningData actor) =>
    [
        UniformBlock.From(FrameUniformKeys.Bones, BonePaletteUbo.For(actor, TotkBindings.Bones)),
        UniformBlock.From(FrameUniformKeys.ShapeMatrix, ShapeMatrixUbo.BuildFromModelMatrix(actor.PlacementRows)),
    ];

    public static UniformBlock[] InstancedPlaceholders { get; } =
    [
        UniformBlock.Zeroed(TotkBindings.Bones),
        UniformBlock.Zeroed(ShapeMatrixUbo.Binding),
    ];
}
