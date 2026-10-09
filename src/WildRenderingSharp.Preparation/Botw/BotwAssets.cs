using BfresLibrary;
using BntxSharp;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Botw;

/// <summary>BotW's models, textures and shader archives, read through an IRomAccess.</summary>
public sealed class BotwAssets(IRomAccess rom) : IGameAssets
{
    const string GraphicsPack = "Pack/Bootup_Graphics.pack";
    static readonly string[] ModelPacks = ["Pack/TitleBG.pack"];

    readonly Dictionary<string, List<BntxFile>> _textures = new(StringComparer.OrdinalIgnoreCase);

    public IRomAccess Rom => rom;

    public byte[]? ReadModel(string modelName)
    {
        byte[]? data = ReadModelFile($"{modelName}.sbfres");
        if (data == null)
            Log.Warning($"[ExportTestBench] no model '{modelName}' under Model/ or in the packs.");
        return data;
    }

    public TextureHandle? FindTexture(string modelName, string name)
    {
        foreach (BntxFile bntx in TextureArchives(modelName))
        {
            foreach (BntxTexture texture in bntx.Textures)
                if (texture.Name == name)
                    return new TextureHandle(surfaces => TexToGo.FromBntx(texture, surfaces));
        }
        return null;
    }

    public IEnumerable<ResFile> AnimationArchives(string modelName, IReadOnlyList<string>? packNames) => [];

    public string ExtractShaderArchive(string name, string directory)
    {
        string path = Path.Combine(directory, name + ".bfsha");
        if (File.Exists(path))
            return path;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, rom.ReadAllBytesNested($"{GraphicsPack}//Shader/{name}.product.sbfsha").ToArray());
        return path;
    }

    List<BntxFile> TextureArchives(string modelName)
    {
        lock (_textures)
        {
            if (_textures.TryGetValue(modelName, out var cached))
                return cached;

            var list = new List<BntxFile>();
            if (ReadModelFile($"{modelName}.Tex.sbfres") is { } data)
            {
                using var stream = new MemoryStream(data);
                var res = new ResFile(stream, false);
                foreach (var external in res.ExternalFiles)
                    if (external.Key.EndsWith(".bntx", StringComparison.OrdinalIgnoreCase))
                        list.Add(BntxFile.Load(external.Value.Data));
            }
            _textures[modelName] = list;
            return list;
        }
    }

    // A model file sits loose under Model/ in some dumps and inside a pack in others.
    byte[]? ReadModelFile(string fileName)
    {
        string loose = $"Model/{fileName}";
        if (rom.Exists(loose))
            return rom.ReadAllBytesNested(loose).ToArray();
        foreach (string pack in ModelPacks)
        {
            string nested = $"{pack}//{loose}";
            if (rom.Exists(nested))
                return rom.ReadAllBytesNested(nested).ToArray();
        }
        return null;
    }
}
