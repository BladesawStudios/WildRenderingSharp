using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Tests;

/// <summary>The profile must hand the shaders exactly the bytes the pipeline used to build inline.</summary>
public class TotkProfileTests
{
    static readonly TotkProfile Profile = new();
    static readonly IWorldBasis World = YUpWorldBasis.Instance;

    static Camera SampleCamera() => new() { Eye = new(4, -6, 3), Target = new(0, 0, 1), FovDegrees = 40f, NearPlane = 0.1f, FarPlane = 3000f };

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(640, 640)]
    public void Camera_matchesInlineConstruction(int width, int height)
    {
        var camera = SampleCamera();
        var vp = camera.BuildViewProjection(width, height);
        var view4 = Mat4Math.ToMat4(vp.View);
        var viewInv3 = Mat4Math.Invert(view4)[..3];
        var viewProj = Mat4Math.Multiply(vp.Proj, view4);
        var texel = new Vector2(1f / width, 1f / height);

        var expected = ContextUbo.BuildForCamera(World.Rows(vp.View), World.Rows(viewProj), vp.Proj, World.InverseRows(viewInv3),
            vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, texel);

        var actual = Profile.Camera("ctx", CameraData.From(camera, width, height));

        Assert.Equal(expected.ToByteArray(), actual.Data);
        Assert.Equal(1u, actual.Binding);
    }

    [Fact]
    public void FlippedCamera_matchesMultiplyingTheFlippedProjection()
    {
        var camera = SampleCamera();
        var vp = camera.BuildViewProjection(1280, 720);
        Vector4[] projFlipped = [vp.Proj[0], -vp.Proj[1], vp.Proj[2], vp.Proj[3]];
        var view4 = Mat4Math.ToMat4(vp.View);
        var viewProjFlipped = Mat4Math.Multiply(projFlipped, view4);
        var viewInv3 = Mat4Math.Invert(view4)[..3];

        var expected = ContextUbo.BuildForCamera(World.Rows(vp.View), World.Rows(viewProjFlipped), projFlipped, World.InverseRows(viewInv3),
            vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, new Vector2(1f / 1280, 1f / 720));

        var actual = Profile.Camera("ctx", CameraData.From(camera, 1280, 720).FlippedY());

        Assert.Equal(expected.ToByteArray(), actual.Data);
    }

    [Fact]
    public void LightCamera_matchesInlineConstruction()
    {
        var camera = SampleCamera();
        var scene = CameraData.From(camera, 1280, 720);
        var light = ShadowPass.BuildLightMatrices(new Vector3(-5), new Vector3(5), Vector3.Normalize(new(0.3f, 0.2f, 0.9f)));

        var expected = ContextUbo.BuildForCamera(World.Rows(light.View3Rows), World.Rows(light.ViewProj), light.Proj,
            World.InverseRows(Mat4Math.Invert(Mat4Math.ToMat4(light.View3Rows))[..3]), 1f, 1f,
            camera.NearPlane, camera.FarPlane, new Vector2(1f / 1280, 1f / 720));

        Assert.Equal(expected.ToByteArray(), Profile.Camera("ctx_light", CameraData.ForLight(light, scene)).Data);
    }

    [Fact]
    public void Lighting_matchesInlineConstruction()
    {
        var sunView = Vector3.Normalize(new(0.3f, 0.8f, 0.5f));
        var sunWorld = Vector3.Normalize(new(-0.4f, 0.2f, 0.9f));
        var sun = new Vector3(2.1f, 1.9f, 1.6f);
        var sky = new Vector3(0.4f, 0.5f, 0.7f);
        var ground = new Vector3(0.2f, 0.18f, 0.15f);
        var mask = new Vector3(0.1f, 0.2f, 0.3f);

        var env = EnvUbo.BuildFromLighting(sunView, World.Direction(sunWorld), sun, sky, ground, mask, 0.5f, 4096);
        var scene = SceneMatUbo.BuildFromLighting(sky, ground, 0.6f, 1.4f);

        var blocks = Profile.Lighting(new SceneLightingData(sunView, sunWorld, sun, sky, ground, mask, 0.5f, 4096, 0.6f, 1.4f));

        Assert.Collection(blocks,
            b => { Assert.Equal(6u, b.Binding); Assert.Equal(env.ToByteArray(), b.Data); },
            b => { Assert.Equal(10u, b.Binding); Assert.Equal(scene.ToByteArray(), b.Data); });
    }

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
