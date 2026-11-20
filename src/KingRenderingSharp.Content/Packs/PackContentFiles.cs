using System.Text.RegularExpressions;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Packs;

/// <summary>One store of a system pack: the game data entries named <c>&lt;store&gt;/&lt;file&gt;</c>, served as the files the renderer asks for by name.</summary>
public sealed class PackContentFiles(AssetPack pack, string store, string stamp) : IContentFiles
{
    readonly string _prefix = store + "/";

    public static string EntryName(string store, string file) => $"{store}/{file}";

    public bool Exists(string name) => pack.Find(PackEntryKind.GameData, _prefix + name) is not null;

    public byte[] ReadBytes(string name) => pack.GameData(_prefix + name) ?? throw new FileNotFoundException($"The pack has no {store} file {name}", name);

    public IEnumerable<string> Enumerate(string pattern)
    {
        Regex match = new("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", RegexOptions.CultureInvariant);
        return pack.Of(PackEntryKind.GameData).Where(e => e.Name.StartsWith(_prefix, StringComparison.Ordinal)).Select(e => e.Name[_prefix.Length..]).Where(n => match.IsMatch(n));
    }

    public string Stamp(string name) => Exists(name) ? stamp : "absent";
}
