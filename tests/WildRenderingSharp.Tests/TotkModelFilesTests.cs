using WildRenderingSharp.Preparation.Totk;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Tests;

public sealed class TotkModelFilesTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("romfs").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    LayeredRom Rom(params string[] models)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Model"));
        foreach (string model in models)
            File.WriteAllBytes(Path.Combine(_root, "Model", model + ".bfres.mc"), [1]);
        return new LayeredRom([_root]);
    }

    [Fact]
    public void TheFullStemIsFoundAsGiven()
    {
        using var rom = Rom("Enemy_Chuchu_Junior.Chuchu_Plain_Junior");

        Assert.Equal("Model/Enemy_Chuchu_Junior.Chuchu_Plain_Junior.bfres.mc", TotkModelFiles.Find(rom, "Enemy_Chuchu_Junior.Chuchu_Plain_Junior"));
    }

    [Fact]
    public void AShortNameWhosePackAndModelCoincideIsDoubled()
    {
        using var rom = Rom("Weapon_Sword_070.Weapon_Sword_070");

        Assert.Equal("Model/Weapon_Sword_070.Weapon_Sword_070.bfres.mc", TotkModelFiles.Find(rom, "Weapon_Sword_070"));
    }

    [Fact]
    public void TheModelHalfAloneIsFoundWhenOnlyOneFileEndsWithIt()
    {
        using var rom = Rom("Enemy_Chuchu_Junior.Chuchu_Elec_Junior", "Enemy_Chuchu_Junior.Chuchu_Plain_Junior");

        Assert.Equal("Model/Enemy_Chuchu_Junior.Chuchu_Elec_Junior.bfres.mc", TotkModelFiles.Find(rom, "Chuchu_Elec_Junior"));
    }

    [Fact]
    public void ANameThatTwoFilesEndWithIsNotGuessedAndIsExplained()
    {
        using var rom = Rom("PackA.Shared", "PackB.Shared");

        Assert.Null(TotkModelFiles.Find(rom, "Shared"));
        Assert.Contains("ambiguous", TotkModelFiles.Explain(rom, "Shared"));
    }

    [Fact]
    public void AMissingNameIsExplainedWithNearMatches()
    {
        using var rom = Rom("Enemy_Chuchu_Junior.Chuchu_Elec_Junior");

        Assert.Null(TotkModelFiles.Find(rom, "Chuchu"));
        Assert.Contains("Enemy_Chuchu_Junior.Chuchu_Elec_Junior", TotkModelFiles.Explain(rom, "Chuchu"));
    }
}
