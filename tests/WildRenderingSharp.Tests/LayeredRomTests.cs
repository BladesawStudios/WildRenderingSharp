using SarcLibrary;
using WildRenderingSharp.Rom;
using Yaz0Sharp;

namespace WildRenderingSharp.Tests;

public sealed class LayeredRomTests : IDisposable
{
    readonly string _baseDir = Directory.CreateTempSubdirectory("rom-base").FullName;
    readonly string _updateDir = Directory.CreateTempSubdirectory("rom-update").FullName;

    public void Dispose()
    {
        Directory.Delete(_baseDir, true);
        Directory.Delete(_updateDir, true);
    }

    void Write(string root, string path, byte[] data)
    {
        string file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, data);
    }

    [Fact]
    public void ALaterFolderReplacesAnEarlierOnesFile()
    {
        Write(_baseDir, "Model/a.bin", [1]);
        Write(_updateDir, "Model/a.bin", [2]);
        using var rom = new LayeredRom([_baseDir, _updateDir]);
        Assert.Equal(new byte[] { 2 }, rom.ReadAllBytesDirectSpan("Model/a.bin").ToArray());
    }

    [Fact]
    public void AFileOnlyInAnEarlierFolderStillShowsThrough()
    {
        Write(_baseDir, "Model/a.bin", [1]);
        using var rom = new LayeredRom([_baseDir, _updateDir]);
        Assert.True(rom.Exists("Model/a.bin"));
        Assert.False(rom.Exists("Model/missing.bin"));
    }

    [Fact]
    public void ADoubleSlashReachesIntoACompressedArchiveAndDecompressesTheEntry()
    {
        byte[] payload = Enumerable.Range(0, 200).Select(i => (byte)(i % 7)).ToArray();
        var sarc = new Sarc { ["Model/x.sbfres"] = Yaz0.Compress(payload) };
        using var packed = new MemoryStream();
        sarc.Write(packed);
        Write(_baseDir, "Pack/Test.pack", Yaz0.Compress(packed.ToArray()));
        using var rom = new LayeredRom([_baseDir]);

        Assert.True(rom.Exists("Pack/Test.pack//Model/x.sbfres"));
        Assert.Equal(payload, rom.ReadAllBytesNested("Pack/Test.pack//Model/x.sbfres").ToArray());
        Assert.Equal(payload, rom.ReadAllBytesCompressedSpan("Pack/Test.pack//Model/x.sbfres").ToArray());
        Assert.NotEqual(payload, rom.ReadAllBytesDirectSpan("Pack/Test.pack//Model/x.sbfres").ToArray());
    }

    [Fact]
    public void ReadingAFileThatIsNotCompressedAsCompressedFails()
    {
        Write(_baseDir, "a.bin", [1, 2, 3, 4, 5]);
        using var rom = new LayeredRom([_baseDir]);
        Assert.Throws<InvalidDataException>(() => rom.ReadAllBytesCompressedSpan("a.bin"));
    }

    [Fact]
    public void EnumerateListsAFoldersFilesAcrossLayersOnce()
    {
        Write(_baseDir, "Model/a.bin", [1]);
        Write(_updateDir, "Model/a.bin", [2]);
        Write(_updateDir, "Model/b.bin", [3]);
        using var rom = new LayeredRom([_baseDir, _updateDir]);
        Assert.Equal(["Model/a.bin", "Model/b.bin"], rom.Enumerate("Model", "*.bin").Order().ToArray());
    }

    [Fact]
    public void ArchivesStillReadCorrectlyWhenTheCacheBudgetForcesThemOut()
    {
        for (int i = 0; i < 3; i++)
        {
            var sarc = new Sarc { [$"Model/{i}.bin"] = Yaz0.Compress(Enumerable.Repeat((byte)i, 300).ToArray()) };
            using var packed = new MemoryStream();
            sarc.Write(packed);
            Write(_baseDir, $"Pack/{i}.pack", Yaz0.Compress(packed.ToArray()));
        }
        using var rom = new LayeredRom([_baseDir], cacheBudget: 1);

        for (int round = 0; round < 2; round++)
            for (int i = 0; i < 3; i++)
                Assert.Equal(Enumerable.Repeat((byte)i, 300).ToArray(), rom.ReadAllBytesNested($"Pack/{i}.pack//Model/{i}.bin").ToArray());
    }

    [Fact]
    public void ArchiveEntriesThatDifferOnlyInCaseAreEachFoundByTheirExactName()
    {
        var sarc = new Sarc { ["res/Dungeon.bin"] = new byte[] { 1 }, ["res/dungeon.bin"] = new byte[] { 2 } };
        using var packed = new MemoryStream();
        sarc.Write(packed);
        Write(_baseDir, "Pack/Case.pack", packed.ToArray());
        using var rom = new LayeredRom([_baseDir]);

        Assert.Equal(new byte[] { 1 }, rom.ReadAllBytesNested("Pack/Case.pack//res/Dungeon.bin").ToArray());
        Assert.Equal(new byte[] { 2 }, rom.ReadAllBytesNested("Pack/Case.pack//res/dungeon.bin").ToArray());
        Assert.True(rom.Exists("Pack/Case.pack//RES/DUNGEON.BIN"));
    }

    [Fact]
    public void LocateNamesTheFileInTheHighestFolderThatHasIt()
    {
        Write(_baseDir, "Model/a.bin", [1]);
        Write(_baseDir, "Model/only-base.bin", [1]);
        Write(_updateDir, "Model/a.bin", [2]);
        using var rom = new LayeredRom([_baseDir, _updateDir]);

        Assert.Equal(Path.Combine(_updateDir, "Model/a.bin"), rom.Locate("Model/a.bin"));
        Assert.Equal(Path.Combine(_baseDir, "Model/only-base.bin"), rom.Locate("Model\\only-base.bin"));
        Assert.Null(rom.Locate("Model/missing.bin"));
    }

    [Fact]
    public void ARecordingNotesTheFileThatAnsweredEachLookupIncludingMisses()
    {
        Write(_baseDir, "Model/a.bin", [1]);
        Write(_updateDir, "Model/a.bin", [2]);
        using var rom = new LayeredRom([_baseDir, _updateDir]);

        using (var recording = rom.Record())
        {
            rom.ReadAllBytesDirectSpan("Model/a.bin");
            rom.Exists("Model/missing.bin");

            Assert.Equal(Path.Combine(_updateDir, "Model/a.bin"), recording.Files["Model/a.bin"]);
            Assert.Null(recording.Files["Model/missing.bin"]);
        }

        using var after = rom.Record();
        rom.Exists("Model/a.bin");
        Assert.Single(after.Files);
    }
}
