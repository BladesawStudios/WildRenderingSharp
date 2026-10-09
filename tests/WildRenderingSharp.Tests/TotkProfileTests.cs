using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Tests;

/// <summary>The profile must hand the shaders the matrices GL-convention math gives, in the layout of the game's blocks.</summary>
public class TotkProfileTests
{
    static readonly TotkProfile Profile = new();
    static readonly IWorldBasis World = YUpWorldBasis.Instance;

    [Fact]
    public void Actor_withoutSkeleton_tilesThePlacementAcrossThePalette()
    {
        Vector4[] placement = [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];
        var gamePlacement = World.PlacementRows(placement);

        var blocks = Profile.Actor(new SkinningData(placement, null, null));

        Assert.Collection(blocks,
            b => { Assert.Equal(2u, b.Binding); Assert.Equal(BonePaletteUbo.FillIdentity(gamePlacement).ToByteArray(), b.Data); },
            b => { Assert.Equal(4u, b.Binding); Assert.Equal(ShapeMatrixUbo.BuildFromModelMatrix(gamePlacement).ToByteArray(), b.Data); });
    }

    [Fact]
    public void InstancedPlaceholders_areZeroedBonesAndShapeMatrix()
    {
        Assert.Collection(Profile.InstancedActorPlaceholders,
            b => { Assert.Equal(2u, b.Binding); Assert.Null(b.Data); },
            b => { Assert.Equal(4u, b.Binding); Assert.Null(b.Data); });
    }
}
