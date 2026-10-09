using WildRenderingSharp.Rom;
using WildRenderingSharp.Rom.Games;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>A romfs opened with the mods layered over it, for model files, and bare, for the shader archives and system assets a mod must not replace.</summary>
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

    public IReadOnlyList<string> ModRoots { get; }

    public LayeredRom Base { get; }

    public LayeredRom Layered { get; }

    public void Dispose()
    {
        if (!ReferenceEquals(Layered, Base))
            Layered.Dispose();
        Base.Dispose();
    }
}
