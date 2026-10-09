using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Tests;

/// <summary>The profile must hand the shaders the matrices GL-convention math gives, in the layout of the game's blocks.</summary>
public class TotkProfileTests
{
    static readonly TotkProfile Profile = new();

    [Fact]
    public void Actor_withoutSkeleton_tilesThePlacementAcrossThePalette()
    {
        Vector4[] placement = [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];

        var blocks = Profile.Actor(new SkinningData(placement, null, null));

        Assert.Collection(blocks,
            b => { Assert.Equal(TotkBlocks.Bones, b.Spec); Assert.Equal(BonePalette.Identity(TotkBlocks.Bones, placement).Bytes.ToArray(), b.Bytes.ToArray()); },
            b => { Assert.Equal(TotkBlocks.ShapeMatrix, b.Spec); Assert.Equal(TotkActorUniforms.ShapeMatrix(placement).Bytes.ToArray(), b.Bytes.ToArray()); });
    }

    [Fact]
    public void InstancedActors_leaveTheBonesAndShapeMatrixZeroed()
    {
        Assert.Equal([TotkBlocks.Bones, TotkBlocks.ShapeMatrix], Profile.InstancedActorBlocks);
        Assert.Equal([2u, 4u], Profile.InstancedActorBlocks.Select(b => b.Binding));
    }
}
