using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Profiles.Totk.Stages;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Tests;

public class UboSnapshotTests
{
    static readonly Vector4[] View =
        [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];

    static readonly Vector3 SunView = Vector3.Normalize(new(0.3f, 0.8f, 0.5f));
    static readonly Vector3 SunWorld = Vector3.Normalize(new(-0.4f, 0.2f, 0.9f));

    static readonly SceneLightingData Lighting = new(
        SunView, SunWorld, new(2.1f, 1.9f, 1.6f), new(0.4f, 0.5f, 0.7f), new(0.2f, 0.18f, 0.15f), new(0.1f, 0.2f, 0.3f), 0.5f, 4096, 0.6f, 1.4f);

    [Fact]
    public void Context_camera() => Snapshot.Verify("context_camera", TotkCameraUniforms.Build("camera", TestCamera.Default));

    [Fact]
    public void Env_lighting() => Snapshot.Verify("env_lighting", TotkLightingUniforms.Build(Lighting)[0]);

    [Fact]
    public void SceneMat_lighting() => Snapshot.Verify("scenemat_lighting", TotkLightingUniforms.Build(Lighting)[1]);

    [Fact]
    public void ShapeMatrix_model() => Snapshot.Verify("shapematrix_model", TotkActorUniforms.ShapeMatrix(View));

    [Fact]
    public void BonePalette_identity() => Snapshot.Verify("bonepalette_identity", BonePalette.Identity(TotkBlocks.Bones));

    [Fact]
    public void BonePalette_identityWithModel() => Snapshot.Verify("bonepalette_identity_model", BonePalette.Identity(TotkBlocks.Bones, View));

    [Fact]
    public void BonePalette_skinned()
    {
        Matrix4x4[] boneWorld =
        [
            Matrix4x4.CreateTranslation(0, 1, 0),
            Matrix4x4.CreateRotationZ(0.4f) * Matrix4x4.CreateTranslation(1, 2, 3),
            Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateTranslation(-2, 0, 1),
        ];
        int[] matrixToBone = [0, 1, 2, 1];
        Matrix4x4[] inverseBind = [Matrix4x4.CreateTranslation(0, -1, 0), Matrix4x4.CreateTranslation(-1, -2, -3), Matrix4x4.Identity];
        var model = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(5, 0, -5);

        Snapshot.Verify("bonepalette_skinned", BonePalette.Posed(TotkBlocks.Bones, boneWorld, matrixToBone, inverseBind, model));
    }

    [Fact]
    public void HdrComposeParams_default() => Snapshot.Verify("hdrcompose_default", TonemapStage.HdrComposeParams());

    [Fact]
    public void SupportBuffer_block() => Snapshot.Verify("support_buffer", SupportBuffer.Block);
}
