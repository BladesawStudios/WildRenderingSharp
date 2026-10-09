using System.Numerics;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Shadows;

namespace WildRenderingSharp.Tests;

public class InstanceBatchPartsTests
{
    static Vector4[] TranslatedBy(float x, float y, float z) =>
        [new(1, 0, 0, x), new(0, 1, 0, y), new(0, 0, 1, z)];

    [Fact]
    public void PackedInstancesRepeatTheirPlacementWhenThereIsNoSkeleton()
    {
        Vector4[] placement = TranslatedBy(10, 0, 0);
        int stride = InstanceData.PaletteOffset + 3;

        var packed = InstanceData.Pack([placement], new Vector3(-1), new Vector3(1), [], stride);

        Assert.Equal(placement, packed.Vectors[..3]);
        Assert.Equal(placement, packed.Vectors[InstanceData.PaletteOffset..(InstanceData.PaletteOffset + 3)]);
    }

    [Fact]
    public void PackedBoundsAreTheModelBoxUnderEveryPlacement()
    {
        var packed = InstanceData.Pack([TranslatedBy(10, 0, 0), TranslatedBy(-10, 5, 0)], new Vector3(-1), new Vector3(1), [], InstanceData.PaletteOffset + 3);

        Assert.Equal(new Vector3(-11, -1, -1), packed.BoundsMin);
        Assert.Equal(new Vector3(11, 6, 1), packed.BoundsMax);
    }

    [Fact]
    public void ShadowRunsCoverTheInstancesInsideTheFocusAsOneStretch()
    {
        var runs = new InstanceShadowRuns([TranslatedBy(0, 0, 0), TranslatedBy(1, 0, 0), TranslatedBy(2, 0, 0), TranslatedBy(100, 0, 0), TranslatedBy(101, 0, 0)], 0f);

        runs.Update(new ShadowFocus(Vector3.Zero, 2f));

        Assert.Equal([(0, 3, 0)], runs.Visible);
    }

    [Fact]
    public void ACascadeKeepsInstancesWithinItsLightSquareAndDrawsFarCascadesCoarser()
    {
        var runs = new InstanceShadowRuns([TranslatedBy(0, 0, 0), TranslatedBy(1, 0, 0), TranslatedBy(50, 0, 0)], 0f);

        runs.UpdateCascade(3, new ShadowFocus(Vector3.Zero, 1f), Vector3.UnitX, Vector3.UnitY, 1.5f);

        Assert.Equal([(0, 2, 2)], runs.Cascade(3));
    }

    [Fact]
    public void ACascadeMaskedOutCastsNothing()
    {
        var runs = new InstanceShadowRuns([TranslatedBy(0, 0, 0)], 0f) { CascadeMask = ~(1 << 1) };

        runs.UpdateCascade(1, new ShadowFocus(Vector3.Zero, 1f), Vector3.UnitX, Vector3.UnitY, 5f);

        Assert.Empty(runs.Cascade(1));
    }

    [Fact]
    public void AnUnbakedBatchPointsEveryInstanceAtNoAtlas()
    {
        var table = InstanceBakeTable.Build(3, []);

        Assert.Equal([-1, -1, -1], table.AtlasOfInstance);
        Assert.Empty(table.Atlases);
        Assert.All(table.Row8, row => Assert.Equal(Vector4.Zero, row));
    }
}
