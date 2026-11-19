using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Assets.Sources;

/// <summary>Binary files by name: what the texture cache reads a texture's bytes from.</summary>
internal interface IFileSource
{
    bool Has(string file);

    byte[] Bytes(string file);
}

/// <summary>The files of a folder, read through the cache's compression.</summary>
internal sealed class FolderFiles(string directory) : IFileSource
{
    public bool Has(string file) => CacheFiles.Exists(Path.Combine(directory, file));

    public byte[] Bytes(string file) => CacheFiles.ReadAllBytes(Path.Combine(directory, file));
}
