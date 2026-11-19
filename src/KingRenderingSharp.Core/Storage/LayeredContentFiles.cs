namespace KingRenderingSharp.Core.Storage;

/// <summary>A store made of others searched in turn, to which more can be added while it is in use: the first that holds a name gives it.</summary>
public sealed class LayeredContentFiles(IContentFiles first) : IContentFiles
{
    readonly List<IContentFiles> _layers = [first];
    readonly object _gate = new();

    public void Add(IContentFiles layer)
    {
        lock (_gate)
            if (!_layers.Contains(layer)) _layers.Add(layer);
    }

    IContentFiles[] Layers()
    {
        lock (_gate) return [.. _layers];
    }

    IContentFiles? Holding(string name) => Layers().FirstOrDefault(l => l.Exists(name));

    public bool Exists(string name) => Holding(name) is not null;

    public byte[] ReadBytes(string name) => (Holding(name) ?? throw new FileNotFoundException($"No store holds {name}", name)).ReadBytes(name);

    public IEnumerable<string> Enumerate(string pattern) => Layers().SelectMany(l => l.Enumerate(pattern)).Distinct(StringComparer.Ordinal);

    public string Stamp(string name) => Holding(name)?.Stamp(name) ?? "absent";
}
