using BfresLibrary;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>TotK's models, textures and animation archives, read through an IRomAccess.</summary>
public sealed class TotkAssets(IRomAccess rom) : IGameAssets
{
    readonly Dictionary<string, byte[]> _models = new(StringComparer.Ordinal);

    public IRomAccess Rom => rom;

    public byte[]? ReadModel(string modelName)
    {
        if (_models.TryGetValue(modelName, out var cached))
            return cached;
        if (TotkModelFiles.Find(rom, modelName) is not { } path)
        {
            Console.WriteLine($"[ExportTestBench] {TotkModelFiles.Explain(rom, modelName)}");
            return null;
        }

        Console.WriteLine($"[ExportTestBench] Decompressing {path}...");
        return _models[modelName] = Mcpk.ToBfres(rom.ReadAllBytesDirectSpan(path).ToArray(), path);
    }

    public TextureHandle? FindTexture(string modelName, string name) => TotkTextures.Handle(rom, name);

    public IEnumerable<ResFile> AnimationArchives(string modelName, IReadOnlyList<string>? packNames)
    {
        IEnumerable<string> paths = packNames is { Count: > 0 } ? NamedArchives(packNames) : ArchivesByPrefix(modelName);
        foreach (string path in paths)
            if (LoadArchive(path) is { } archive)
                yield return archive;
    }

    // The pack names the actor lists are exact; a name that is not there is skipped with a note.
    IEnumerable<string> NamedArchives(IReadOnlyList<string> packNames)
    {
        foreach (string pack in packNames)
        {
            string path = $"Model/{pack}.anim.bfres.zs";
            if (rom.Exists(path))
                yield return path;
            else
                Console.WriteLine($"[ExportTestBench] Actor named anim archive '{pack}.anim.bfres.zs' not found under Model/ - skipping.");
        }
    }

    // Without an actor pack to ask, the archives sharing the model's pack prefix are the best guess.
    IEnumerable<string> ArchivesByPrefix(string modelName) =>
        rom.Enumerate("Model", $"{modelName.Split('.')[0]}*.anim.bfres.zs").ToList();

    ResFile? LoadArchive(string path)
    {
        byte[] bytes;
        try
        {
            bytes = rom.ReadAllBytesNested(path).ToArray();
        }
        catch (IOException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExportTestBench] SKIPPED anim archive {path}: decompress failed ({ex.Message})");
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new ResFile(stream, false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExportTestBench] SKIPPED anim archive {path}: not a readable BFRES ({ex.Message})");
            return null;
        }
    }
}
