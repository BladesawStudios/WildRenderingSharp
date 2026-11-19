using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using KingRenderingSharp.Content.Animation.Clips;
using KingRenderingSharp.Content.Animation.Posing;
using KingRenderingSharp.Content.Packs;

namespace KingRenderingSharp.Tests;

/// <summary>The animation files exported from a pack: how a curve is read, how a visibility curve evaluates, and, with KRS_PARITY set, that every material, texture pattern and visibility animation of a real pack is written and loads.</summary>
public sealed class PackAnimationTests
{
    static CurveRecord Curve(ushort flags, float[] frames, float[] keys, int[]? ints = null, byte[]? bools = null) =>
        new(flags, 0, 0, frames.Length == 0 ? 0 : frames[^1], 1, 0, 0, 0, frames, keys, ints ?? [], bools ?? [], []);

    [Fact]
    public void ASteppedIntegerCurveWhoseKeysAreFloatsKeepsItsValues()
    {
        PackCurve? curve = PackCurve.Read(Curve(0x4a, [0, 3, 6], [2, 0, 1]), out string? reason);
        Assert.Null(reason);
        Assert.NotNull(curve);
        Assert.Equal(0x40, curve.Type);
        Assert.Equal([2f, 0f, 1f], curve.Rows.Select(r => r[0]));
    }

    [Fact]
    public void ABooleanCurveReadsItsBytes()
    {
        PackCurve? curve = PackCurve.Read(Curve(0x60, [0, 5], [], bools: [1, 0]), out _);
        Assert.NotNull(curve);
        Assert.Equal([1f, 0f], curve.Rows.Select(r => r[0]));
    }

    [Fact]
    public void ALinearCurveGetsARowOfTwoKeysForEachFrame()
    {
        PackCurve? curve = PackCurve.Read(Curve(0x10, [0, 10], [1, 0.5f, 2, 0]), out _);
        Assert.NotNull(curve);
        Assert.Equal(2, curve.Rows.Count);
        Assert.Equal([1f, 0.5f], curve.Rows[0]);
    }

    [Fact]
    public void ACurveWithNoKeysIsLeftOutWithAReason()
    {
        Assert.Null(PackCurve.Read(Curve(0x10, [0, 10], []), out string? reason));
        Assert.Contains("keys", reason);
    }

    [Fact]
    public void AVisibilityEntryFollowsItsCurveAndFallsBackToItsBase()
    {
        VisibilityAnimEntry driven = new()
        {
            Name = "Mt_Eye", Base = true,
            Curve = new VisibilityAnimCurve { CurveType = 0x60, StartFrame = 0, EndFrame = 10, Frames = [0, 4, 8], Keys = [[1], [0], [1]] },
        };
        Assert.True(driven.VisibleAt(2));
        Assert.False(driven.VisibleAt(5));
        Assert.True(driven.VisibleAt(9));
        Assert.False(new VisibilityAnimEntry { Name = "Off", Base = false }.VisibleAt(3));
        Assert.True(new VisibilityAnimEntry { Name = "On", Base = true }.VisibleAt(3));
    }

    [Fact]
    public void AStepIntegerCurveHoldsItsValueBetweenKeys()
    {
        Assert.Equal(3, AnimCurveEval.EvaluateInt(0, 10, 0, [0, 4], [[3], [7]], 3.9f));
        Assert.Equal(7, AnimCurveEval.EvaluateInt(0, 10, 0, [0, 4], [[3], [7]], 4f));
    }

    static string? Folder => Environment.GetEnvironmentVariable("KRS_PARITY") is { Length: > 0 } f && File.Exists(Path.Combine(f, "model.bfap")) ? f : null;

    sealed class PackFactAttribute : FactAttribute
    {
        public PackFactAttribute()
        {
            if (Folder is null) Skip = "Set KRS_PARITY to a folder holding model.bfap to run this test.";
        }
    }

    [PackFact]
    public void EveryMaterialPatternAndVisibilityAnimationOfAPackLoadsFromTheSource()
    {
        using AssetPack pack = AssetPack.Open(Path.Combine(Folder!, "model.bfap"));
        string model = Environment.GetEnvironmentVariable("KRS_PARITY_MODEL") ?? "Npc_Zelda_AncientHyrule";
        string packModel = pack.Of(PackEntryKind.Model).Select(e => e.Name).First(n => n == model || n.StartsWith(model + "/", StringComparison.Ordinal));
        string[] stems = [.. pack.Of(PackEntryKind.MaterialAnim).Concat(pack.Of(PackEntryKind.VisibilityAnim)).Concat(pack.Of(PackEntryKind.ShapeAnim)).Select(e => e.Name[..e.Name.IndexOf('/')]).Append(model).Distinct()];
        using PackTextureFiles textures = new(pack);
        PackModelSource source = new(pack, new PackPrograms(new Dictionary<string, BfShaderSharp.Prepared.PreparedArchive>()), textures, packModel, model, stems);

        foreach (string name in source.MaterialAnims)
        {
            MaterialAnimManifest clip = source.MaterialAnim(name);
            Assert.NotEmpty(clip.Materials);
            foreach (MaterialAnimTarget target in clip.Materials.SelectMany(m => m.Targets))
            {
                foreach (float frame in new[] { 0f, clip.FrameCount / 2f, clip.FrameCount })
                    Assert.True(float.IsFinite(BitConverter.UInt32BitsToSingle(target.Bits(frame))) || target.IsInt, $"{name}: {target.Param} at {frame}");
            }
        }

        foreach (string name in source.TexturePatternAnims)
        {
            TexturePatternAnimManifest clip = source.TexturePatternAnim(name);
            foreach (TexturePatternSamplerEntry sampler in clip.Materials.SelectMany(m => m.Samplers))
                Assert.InRange(sampler.Evaluate(0), -1, Math.Max(0, clip.Textures.Count - 1));
        }

        foreach (string name in source.VisibilityAnims)
            foreach (VisibilityAnimEntry entry in source.VisibilityAnim(name).Entries) _ = entry.VisibleAt(0);
    }
}
