using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// A romfs opened twice: with the mods layered over it, for the files that belong to a model, and bare, for the shader archives
/// and system assets every model shares, which a mod must not replace.
/// </summary>
public sealed class TotkRomfs : IDisposable
{
    public TotkRomfs(string root, IEnumerable<string>? modRoots = null)
    {
        Root = Path.GetFullPath(root);
        ModRoots = (modRoots ?? []).Where(Directory.Exists).Select(Path.GetFullPath).ToList();
        Base = TotkRom.Open(Root);
        Layered = ModRoots.Count == 0 ? Base : TotkRom.Open(Root, ModRoots);
    }

    public string Root { get; }

    /// <summary>The mods' romfs folders, highest priority first.</summary>
    public IReadOnlyList<string> ModRoots { get; }

    public LayeredRom Base { get; }

    /// <summary>The same files with the mods over them; <see cref="Base"/> itself when there are none.</summary>
    public LayeredRom Layered { get; }

    public void Dispose()
    {
        if (!ReferenceEquals(Layered, Base))
            Layered.Dispose();
        Base.Dispose();
    }
}
