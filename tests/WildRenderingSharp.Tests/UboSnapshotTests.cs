using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Shaders;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Tests;

public class UboSnapshotTests
{
    static readonly Vector4[] View =
        [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];

    static readonly Vector4[] Proj =
        [new(1.2f, 0, 0, 0), new(0, 2.1f, 0, 0), new(0, 0, -1.002f, -0.2002f), new(0, 0, -1, 0)];

    static readonly Vector4[] ViewProj =
        [new(0.96f, 0.12f, -0.24f, 3.6f), new(-0.21f, 1.89f, 0.63f, -3.15f), new(-0.2f, 0.3f, -0.85f, -12.2f), new(-0.2f, 0.3f, -0.85f, -12f)];

    static readonly Vector4[] ViewInv =
        [new(0.8f, -0.1f, 0.2f, 1f), new(0.1f, 0.9f, -0.3f, 2f), new(-0.2f, 0.3f, 0.85f, -9f)];

    static readonly Vector3 SunView = Vector3.Normalize(new(0.3f, 0.8f, 0.5f));
    static readonly Vector3 SunWorld = Vector3.Normalize(new(-0.4f, 0.2f, 0.9f));

    static ContextUbo Context() =>
        ContextUbo.BuildForCamera(View, ViewProj, Proj, ViewInv, 16f / 9f, 0.55f, 0.1f, 5000f, new Vector2(1f / 1920, 1f / 1080));

    [Fact]
    public void Context_camera() => Snapshot.Verify("context_camera", Context().ToByteArray());

    [Fact]
    public void Context_flipped() => Snapshot.Verify("context_flipped", Context().WithFlippedProjectionY().ToByteArray());

    [Fact]
    public void Env_lighting() => Snapshot.Verify("env_lighting",
        EnvUbo.BuildFromLighting(SunView, SunWorld, new(2.1f, 1.9f, 1.6f), new(0.4f, 0.5f, 0.7f), new(0.2f, 0.18f, 0.15f),
            new(0.1f, 0.2f, 0.3f), 0.5f, 4096).ToByteArray());

    [Fact]
    public void SceneMat_lighting() => Snapshot.Verify("scenemat_lighting",
        SceneMatUbo.BuildFromLighting(new(0.4f, 0.5f, 0.7f), new(0.2f, 0.18f, 0.15f), 0.6f, 1.4f).ToByteArray());

    [Fact]
    public void ShapeMatrix_model() => Snapshot.Verify("shapematrix_model",
        ShapeMatrixUbo.BuildFromModelMatrix(View).ToByteArray());

    [Fact]
    public void BonePalette_identity() => Snapshot.Verify("bonepalette_identity", BonePaletteUbo.FillIdentity(TotkBindings.Bones).ToByteArray());

    [Fact]
    public void BonePalette_identityWithModel() => Snapshot.Verify("bonepalette_identity_model", BonePaletteUbo.FillIdentity(TotkBindings.Bones, View).ToByteArray());

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

        Snapshot.Verify("bonepalette_skinned", BonePaletteUbo.Build(boneWorld, matrixToBone, inverseBind, TotkBindings.Bones, model).ToByteArray());
    }

    [Fact]
    public void HdrComposeParams_default() => Snapshot.Verify("hdrcompose_default", HdrComposeParamsUbo.BuildDefault().ToByteArray());

    [Fact]
    public void SupportBuffer() => Snapshot.Verify("support_buffer", SupportBufferUbo.Build());
}
