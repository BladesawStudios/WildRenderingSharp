using System.Numerics;
using System.Text;
using System.Text.Json;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>One placed actor's baked lighting: its tile's atlas, and each material's region of it by material index.</summary>
/// <param name="Atlas">The atlas texture (BC4, its whole mip chain).</param>
/// <param name="StByMaterial">Indexed by the model's material index: (scale x, scale y, offset x, offset y) into the atlas; zero where a material has no region.</param>
/// <param name="MaterialIndexByName">The model's material index for each material name the bake lists.</param>
public sealed record BakeActor(LoadedTexture Atlas, Vector4[] StByMaterial, IReadOnlyDictionary<string, int> MaterialIndexByName);

/// <summary>
/// The game's baked lighting for placed static actors, read from a cache the preparer fills
/// (<c>prepare-bake</c>; see <c>ShaderLibrary.CompileTool.ExportBake</c>).
/// </summary>
/// <remarks>
/// Every static world object's material samples <c>bake0</c> at <c>aTexCoordBake</c> - the
/// lightmap UV - scaled and offset into its bake tile's atlas: the ambient occlusion and sky
/// shadowing the game baked for that one placement. Without it each samples
/// <c>CmnTex_BakeDefault</c>, a flat 1.0: no contact darkening anywhere, which is most of why a
/// rock face reads as flat and over-lit beside the game's. A placement is found by its hash: the
/// index maps every hash to its tile, and a tile is loaded the first time one of its placements is
/// asked for, if it has been exported (<see cref="MissingTiles"/> says which still need to be).
/// </remarks>
public sealed class BakeLibrary : IDisposable
{
    readonly string _dir;
    readonly TextureCache _textures;
    ulong[] _hashes = [];
    int[] _tileOfHash = [];
    string[] _tiles = [];
    bool _indexLoaded;
    readonly Dictionary<string, Tile?> _loaded = new(StringComparer.Ordinal);

    sealed record Tile(LoadedTexture?[] Textures, Dictionary<ulong, JsonElement> Actors);

    public BakeLibrary(GL gl, string bakeDirectory)
    {
        _dir = bakeDirectory;
        _textures = new TextureCache(gl, bakeDirectory);
    }

    /// <summary>Whether the hash-to-tile index exists yet - the preparer builds it on its first bake run.</summary>
    public bool HasIndex => File.Exists(Path.Combine(_dir, "index.bin"));

    /// <summary>The tile a placement's bake is in, or null when it has none (or there is no index yet).</summary>
    public string? TileOf(ulong hash)
    {
        lock (_sync)
            return TileOfLocked(hash);
    }

    /// <summary>Held for any use: a host may look bakes up on a loading thread.</summary>
    readonly object _sync = new();

    string? TileOfLocked(ulong hash)
    {
        if (!EnsureIndex())
            return null;
        int at = Array.BinarySearch(_hashes, hash);
        return at >= 0 ? _tiles[_tileOfHash[at]] : null;
    }

    /// <summary>Of the tiles these placements need, the ones not exported yet.</summary>
    public IReadOnlyList<string> MissingTiles(IEnumerable<ulong> hashes)
    {
        lock (_sync)
            return [.. hashes.Select(TileOfLocked).OfType<string>().Distinct(StringComparer.Ordinal)
                .Where(t => !File.Exists(Path.Combine(_dir, t + ".json")))];
    }

    /// <summary>A placement's bake, or null when it has none or its tile is not exported yet.</summary>
    public BakeActor? Find(ulong hash)
    {
        lock (_sync)
            return FindLocked(hash);
    }

    BakeActor? FindLocked(ulong hash)
    {
        if (TileOfLocked(hash) is not { } tileName || LoadTile(tileName) is not { } tile)
            return null;
        if (!tile.Actors.TryGetValue(hash, out var actor))
            return null;

        int count = actor.GetProperty("count").GetInt32();
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var st = new Vector4[Math.Max(0, count)];
        LoadedTexture? atlas = null;
        foreach (var mat in actor.GetProperty("materials").EnumerateArray())
        {
            int index = mat.GetProperty("index").GetInt32();
            int texture = mat.GetProperty("texture").GetInt32();
            byName[mat.GetProperty("name").GetString() ?? ""] = index;
            if (texture < 0 || texture >= tile.Textures.Length || tile.Textures[texture] is not { } t)
                continue;
            // One atlas per tile in every file read so far; a material on another would need
            // runs split per material, which nothing has called for yet.
            atlas ??= t;
            if (!ReferenceEquals(atlas, t))
                continue;
            if (index >= st.Length)
                Array.Resize(ref st, index + 1);
            var v = mat.GetProperty("st");
            st[index] = new Vector4(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle(), v[3].GetSingle());
        }
        return atlas is null ? null : new BakeActor(atlas, st, byName);
    }

    bool EnsureIndex()
    {
        if (_indexLoaded)
            return _hashes.Length > 0;
        string path = Path.Combine(_dir, "index.bin");
        if (!File.Exists(path))
            return false;
        _indexLoaded = true;
        using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WRSB" || r.ReadInt32() != 1)
            return false;
        _tiles = new string[r.ReadInt32()];
        for (int i = 0; i < _tiles.Length; i++)
            _tiles[i] = r.ReadString();
        int n = r.ReadInt32();
        _hashes = new ulong[n];
        _tileOfHash = new int[n];
        for (int i = 0; i < n; i++)
        {
            _hashes[i] = r.ReadUInt64();
            _tileOfHash[i] = r.ReadInt32();
        }
        return n > 0;
    }

    /// <summary>Forgets the index and the tiles that were missing, after the preparer has exported more.</summary>
    public void Refresh()
    {
        lock (_sync)
            RefreshLocked();
    }

    void RefreshLocked()
    {
        // The index is read once it exists; re-reading its 600,000 entries on every refresh was a
        // stall of its own.
        if (_hashes.Length == 0)
            _indexLoaded = false;
        foreach (var key in _loaded.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
            _loaded.Remove(key);
    }

    Tile? LoadTile(string name)
    {
        if (_loaded.TryGetValue(name, out var cached))
            return cached;
        string path = Path.Combine(_dir, name + ".json");
        if (!File.Exists(path))
        {
            _loaded[name] = null;
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var textures = root.GetProperty("textures").EnumerateArray().Select(t => _textures.Load(new SamplerBinding
        {
            Key = "bake0",
            Assigned = "bake0",
            Texture = t.GetProperty("name").GetString() ?? "",
            File = t.GetProperty("file").GetString() ?? "",
            Format = t.GetProperty("format").GetString() ?? "",
            Width = t.GetProperty("width").GetInt32(),
            Height = t.GetProperty("height").GetInt32(),
            WrapU = "Clamp",
            WrapV = "Clamp",
        })).ToArray();
        var actors = new Dictionary<ulong, JsonElement>();
        foreach (var a in root.GetProperty("actors").EnumerateObject())
            if (ulong.TryParse(a.Name, out ulong hash))
                actors[hash] = a.Value.Clone();
        var tile = new Tile(textures, actors);
        _loaded[name] = tile;
        return tile;
    }

    public void Dispose() => _textures.Dispose();
}
