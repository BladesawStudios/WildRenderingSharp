using System.Buffers.Binary;
using System.Numerics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Profiles.Totk.Gloom;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Tests;

// The gloom programs read these blocks by slot and component; the checks here run the program's own arithmetic over the block and compare with the matrices it came from.
public class GloomBlocksTests
{
    const int Width = 1600, Height = 900;

    static readonly Camera Eye = new() { Eye = new(120f, 40f, -75f), Target = new(10f, 5f, 30f), FovDegrees = 62f, NearPlane = 0.3f, FarPlane = 20000f };

    static readonly CameraData Cam = CameraData.From(Eye, Width, Height);

    static Vector4 Row(Ubo ubo, int slot) => new(
        BinaryPrimitives.ReadSingleLittleEndian(ubo.Bytes.Span[(slot * 16)..]), BinaryPrimitives.ReadSingleLittleEndian(ubo.Bytes.Span[(slot * 16 + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(ubo.Bytes.Span[(slot * 16 + 8)..]), BinaryPrimitives.ReadSingleLittleEndian(ubo.Bytes.Span[(slot * 16 + 12)..]));

    // A 3x4 matrix held as rows, applied to a point.
    static Vector3 Apply(Ubo ubo, int firstSlot, Vector3 p)
    {
        var h = new Vector4(p, 1f);
        return new(Vector4.Dot(Row(ubo, firstSlot), h), Vector4.Dot(Row(ubo, firstSlot + 1), h), Vector4.Dot(Row(ubo, firstSlot + 2), h));
    }

    static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) => Assert.True((expected - actual).Length() < tolerance, $"{expected} vs {actual}");

    [Fact]
    public void TheViewBlockCarriesTheCameraAsTheProgramsReadIt()
    {
        var block = GloomBlocks.BuildView(Cam, Width, Height);
        Vector3 world = new(50f, 12f, -9f);

        Vector3 view = Vector3.Transform(world, Cam.View);
        Near(view, Apply(block, 0, world));
        Near(world, Apply(block, 7, view));

        // The projection rows give the clip position; the program turns the window depth back into view depth with slot 11.
        var h = new Vector4(view, 1f);
        var clip = new Vector4(Vector4.Dot(Row(block, 3), h), Vector4.Dot(Row(block, 4), h), Vector4.Dot(Row(block, 5), h), Vector4.Dot(Row(block, 6), h));
        float depth = (clip.Z / clip.W) * 0.5f + 0.5f;
        Vector4 range = Row(block, 11);
        Assert.Equal(view.Z, -range.Y / (1f - depth * range.Z), 1e-2f);

        Assert.Equal(Cam.TanHalf.X, Row(block, 10).X, 1e-5f);
        Assert.Equal(Cam.TanHalf.Y, Row(block, 10).Y, 1e-5f);
        Assert.Equal(Width / 2f, Row(block, 12).X);
        Assert.Equal(2f / Height, Row(block, 12).W, 1e-7f);
        Assert.Equal(1f, Row(block, 13).X * Row(block, 13).Y, 1e-6f);
    }

    [Fact]
    public void ABoxBlockTakesAViewPointToTheBoxesUnitSpace()
    {
        var unitToWorld = Matrix4x4.CreateScale(40f, 12f, 25f) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(30f, 8f, 4f);
        var volume = new GloomVolume(GloomShape.Box, unitToWorld, new(0.7f, 0.7f), Vector2.One, 0f, false);
        var block = GloomBlocks.BuildArea(volume, Cam, false)!;

        Vector3 inside = Vector3.Transform(new Vector3(0.2f, -0.3f, 0.45f), unitToWorld);
        Near(new(0.2f, -0.3f, 0.45f), Apply(block, 3, Vector3.Transform(inside, Cam.View)));
        // The vertex stage places the unit box with the first three rows.
        Near(inside, Apply(block, 0, new(0.2f, -0.3f, 0.45f)));
        Assert.Equal(0.7f, Row(block, 6).X);
        Assert.Equal(1f, Row(block, 6).Z);
    }

    [Fact]
    public void ACancellingVolumeIsDrawnAtFadeZero()
    {
        var volume = new GloomVolume(GloomShape.Box, Matrix4x4.CreateScale(10f), new(0.5f, 0.5f), Vector2.One, 0f, true);

        Assert.Equal(0f, Row(GloomBlocks.BuildArea(volume, Cam, true)!, 6).Z);
    }

    [Fact]
    public void ASphereBlockHoldsItsCentreInViewSpaceAndItsRadiusOverTheProgramsCutoff()
    {
        var unitToWorld = Matrix4x4.CreateScale(60f) * Matrix4x4.CreateTranslation(-20f, 3f, 90f);
        var volume = new GloomVolume(GloomShape.Sphere, unitToWorld, new(0.9f, 0.9f), Vector2.One, 0f, false);
        var block = GloomBlocks.BuildArea(volume, Cam, false)!;

        Vector4 sphere = Row(block, 3);
        Near(Vector3.Transform(new Vector3(-20f, 3f, 90f), Cam.View), new(sphere.X, sphere.Y, sphere.Z));
        Assert.Equal(30f / 0.88f, sphere.W, 1e-3f);
        Assert.Equal(0.9f, Row(block, 4).X);
        Assert.Equal(1f, Row(block, 4).Z);
    }

    [Fact]
    public void ACylinderBlockKeepsItsTwoDensitiesItsNoiseScaleAndItsInnerRatios()
    {
        var volume = new GloomVolume(GloomShape.Cylinder, Matrix4x4.CreateScale(30f, 200f, 30f), new(0.4f, -0.2f), new(0.9f, 0.3f), 2.5f, false);
        var block = GloomBlocks.BuildArea(volume, Cam, false)!;

        Assert.Equal(new Vector4(0.4f, -0.2f, 2.5f, 1f), Row(block, 6));
        Assert.Equal(new Vector2(0.9f, 0.3f), new Vector2(Row(block, 7).X, Row(block, 7).Y));
    }

    [Fact]
    public void AFlatBoxHasNoBlock()
    {
        var volume = new GloomVolume(GloomShape.Box, Matrix4x4.CreateScale(10f, 0f, 10f), Vector2.One, Vector2.One, 0f, false);

        Assert.Null(GloomBlocks.BuildArea(volume, Cam, false));
    }

    [Fact]
    public void TheComposeBlockPutsEachParameterAtItsSlot()
    {
        var look = new GloomLook(new(1, 2, 3, 4), new(5, 6, 7, 8), new(9, 10, 11), new(12, 13, 14), new(15, 16), new(17, 18), new(19, 20, 21), new(22, 23), new(24, 25, 26));
        var block = GloomBlocks.BuildCompose(look);

        Assert.Equal(new Vector4(1, 2, 3, 4), Row(block, 1));
        Assert.Equal(new Vector4(5, 6, 7, 8), Row(block, 2));
        Assert.Equal(new Vector3(9, 10, 11), new Vector3(Row(block, 3).X, Row(block, 3).Y, Row(block, 3).Z));
        Assert.Equal(new Vector3(12, 13, 14), new Vector3(Row(block, 4).X, Row(block, 4).Y, Row(block, 4).Z));
        Assert.Equal(new Vector2(15, 16), new Vector2(Row(block, 5).X, Row(block, 5).Y));
        Assert.Equal(new Vector2(17, 18), new Vector2(Row(block, 6).X, Row(block, 6).Y));
        Assert.Equal(new Vector3(19, 20, 21), new Vector3(Row(block, 7).X, Row(block, 7).Y, Row(block, 7).Z));
        Assert.Equal(new Vector2(22, 23), new Vector2(Row(block, 8).X, Row(block, 8).Y));
        Assert.Equal(new Vector3(24, 25, 26), new Vector3(Row(block, 9).X, Row(block, 9).Y, Row(block, 9).Z));
    }
}
