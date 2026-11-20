using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Assets.Sources;

/// <summary>Binary files by name: what the texture cache reads a texture's bytes from.</summary>
internal interface IFileSource
{
    bool Has(string file);

    byte[] Bytes(string file);
}
