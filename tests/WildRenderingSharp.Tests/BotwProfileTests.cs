using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Profiles.Botw;
using WildRenderingSharp.Profiles.Botw.Shaders;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Tests;

public sealed class BotwProfileTests
{
    static readonly BotwProfile Profile = new();

    [Fact]
    public void AnActorWithoutASkeletonGetsOnlyABonePaletteTilingItsPlacement()
    {
        Vector4[] placement = [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];

        var blocks = Profile.Actor(new SkinningData(placement, null, null));

        var bones = Assert.Single(blocks);
        Assert.Equal(BotwBindings.Bones, bones.Spec.Binding);
        Assert.Equal(BonePalette.Identity(bones.Spec, placement).Bytes.ToArray(), bones.Bytes.ToArray());
    }

    [Fact]
    public void InstancedActorsLeaveTheBonePaletteZeroed()
    {
        var bones = Assert.Single(Profile.InstancedActorBlocks);

        Assert.Equal(BotwBindings.Bones, bones.Binding);
    }

    [Fact]
    public void TheShadersBlocksAreRenumberedToTheProfilesBindings()
    {
        var sources = new BotwShaderSources();
        string cleaned = sources.Clean(
            "layout (binding = 0, std140) uniform _Context { vec4 data[4]; } vp_c3;\n" +
            "layout (binding = 4, std140) uniform _Mat { vec4 data[4]; } fp_c7;\n" +
            "layout (binding = -3, std140) uniform _Driver { vec4 data[4]; } fp_c0;");

        Assert.Contains($"binding = {BotwBindings.Camera}, std140) uniform _Context", cleaned);
        Assert.Contains($"binding = {BotwBindings.Material}, std140) uniform _Mat", cleaned);
        Assert.Contains($"binding = {BotwBindings.Orphan}, std140) uniform _Driver", cleaned);
    }
}
