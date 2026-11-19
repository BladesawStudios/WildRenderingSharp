using KingRenderingSharp.Gpu;
using System.Numerics;
using BfShaderSharp.Pack;
using KingRenderingSharp.Content.Assets.Sources;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Content.Assets.Textures;

namespace KingRenderingSharp.Content.Assets.Baking;

/// <summary>
/// The game's baked lighting for placed static actors, read from the packs the preparer fills (<c>prepare-bake</c>): one holds the index of every placement, and each of the others the tiles
/// it was asked for, with their atlas textures.
/// </summary>
public sealed class BakeLibrary : IDisposable
{
    readonly GpuApi _gl;
    readonly string _dir;
    readonly TextureCache _textures;
    readonly BakeFiles _files = new();
    readonly List<(AssetPack Pack, PackTextureFiles Textures)> _packs = [];
    readonly HashSet<string> _opened = new(StringComparer.OrdinalIgnoreCase);
    // Each atlas's own sampling, as uploaded, and whether the finest-level-only setting is on it now.
    readonly Dictionary<uint, (int MaxLevel, int MinFilter, bool FinestOnly)> _sampling = [];
    ulong[] _hashes = [];
    int[] _tileOfHash = [];
    string[] _tiles = [];
    bool _indexLoaded;
    readonly Dictionary<string, Tile?> _loaded = new(StringComparer.Ordinal);

    sealed record BakeMaterial(string Name, int Index, int Texture, Vector4 St);

    sealed record BakeActorData(int Count, List<BakeMaterial> Materials);

    sealed record Tile(LoadedTexture?[] Textures, Dictionary<ulong, BakeActorData> Actors);

    // The texture files of the tiles loaded so far, by the names the texture cache is asked for, held in memory for as long as the cache reads them.
    sealed class BakeFiles : IFileSource
    {
        readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);

        public void Add(string file, byte[] bytes) => _bytes[file] = bytes;

        public bool Has(string file) => _bytes.ContainsKey(file);

        public byte[] Bytes(string file) => _bytes[file];
    }

    public BakeLibrary(GpuApi gl, string bakeDirectory)
    {
        _gl = gl;
        _dir = bakeDirectory;
        _textures = new TextureCache(gl, _files);
        OpenNewPacks();
    }

    /// <summary>
    /// Whether the atlases are sampled at their finest level only. A mesh edge where the bake coordinates jump gives the pixels beside it derivatives that pick
    /// the coarsest level, which averages the whole atlas into a dark line along the edge.
    /// </summary>
    public bool SamplesFinestLevelOnly { get; set; }

    /// <summary>Puts <see cref="SamplesFinestLevelOnly"/> on every atlas loaded so far, which has to be called on the render thread.</summary>
    public void ApplySampling()
    {
        lock (_sync)
            foreach (var texture in _loaded.Values.Where(t => t is not null).SelectMany(t => t!.Textures).OfType<LoadedTexture>())
                ApplySampling(texture.Handle);
    }

    void ApplySampling(uint handle)
    {
        if (_sampling.TryGetValue(handle, out var known) && known.FinestOnly == SamplesFinestLevelOnly)
            return;

        _gl.BindTexture(TextureTarget.Texture2D, handle);
        if (known == default)
        {
            _gl.GetTexParameter(TextureTarget.Texture2D, (GetTextureParameter)GLEnum.TextureMaxLevel, out int maxLevel);
            _gl.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureMinFilter, out int minFilter);
            known = (maxLevel, minFilter, false);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, SamplesFinestLevelOnly ? 0 : known.MaxLevel);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, SamplesFinestLevelOnly ? (int)GLEnum.Linear : known.MinFilter);
        _sampling[handle] = (known.MaxLevel, known.MinFilter, SamplesFinestLevelOnly);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public bool HasIndex
    {
        get
        {
            lock (_sync)
                return _packs.Any(p => p.Pack.Find(PackEntryKind.GameData, IndexEntry) is not null);
        }
    }

    const string IndexEntry = "Bake/index";

    const string TilePrefix = "Bake/tile/";

    public string? TileOf(ulong hash)
    {
        lock (_sync)
            return TileOfLocked(hash);
    }

    readonly object _sync = new();

    string? TileOfLocked(ulong hash)
    {
        if (!EnsureIndex())
            return null;
        int at = Array.BinarySearch(_hashes, hash);
        return at >= 0 ? _tiles[_tileOfHash[at]] : null;
    }

    public IReadOnlyList<string> MissingTiles(IEnumerable<ulong> hashes)
    {
        lock (_sync)
        {
            OpenNewPacks();
            return [.. hashes.Select(TileOfLocked).OfType<string>().Distinct(StringComparer.Ordinal).Where(t => FindTilePack(t) is null)];
        }
    }

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

        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var st = new Vector4[Math.Max(0, actor.Count)];
        LoadedTexture? atlas = null;
        foreach (var mat in actor.Materials)
        {
            byName[mat.Name] = mat.Index;
            if (mat.Texture < 0 || mat.Texture >= tile.Textures.Length || tile.Textures[mat.Texture] is not { } t)
                continue;
            // One atlas per tile in every file read so far; a material on another would need
            // runs split per material, which nothing has called for yet.
            atlas ??= t;
            if (!ReferenceEquals(atlas, t))
                continue;
            if (mat.Index >= st.Length)
                Array.Resize(ref st, mat.Index + 1);
            st[mat.Index] = mat.St;
        }
        return atlas is null ? null : new BakeActor(atlas, st, byName);
    }

    // Opens the packs of the folder that were not open yet. The preparer moves a pack into place once it is whole, so one that is there can be read.
    void OpenNewPacks()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (string file in Directory.EnumerateFiles(_dir, "*.bfgd").OrderBy(f => File.GetLastWriteTimeUtc(f)))
        {
            if (!_opened.Add(file)) continue;
            try
            {
                AssetPack pack = AssetPack.Open(file);
                _packs.Add((pack, new PackTextureFiles(pack)));
            }
            catch (IOException)
            {
                _opened.Remove(file);
            }
        }
    }

    (AssetPack Pack, PackTextureFiles Textures)? FindTilePack(string tile)
    {
        for (int i = _packs.Count - 1; i >= 0; i--)
            if (_packs[i].Pack.Find(PackEntryKind.GameData, TilePrefix + tile) is not null) return _packs[i];
        return null;
    }

    bool EnsureIndex()
    {
        if (_indexLoaded)
            return _hashes.Length > 0;
        byte[]? bytes = null;
        for (int i = _packs.Count - 1; i >= 0 && bytes is null; i--)
            bytes = _packs[i].Pack.GameData(IndexEntry);
        if (bytes is null)
            return false;
        _indexLoaded = true;
        using var r = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.UTF8);
        if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)) != "KRSB" || r.ReadInt32() != 1)
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

    public void Refresh()
    {
        lock (_sync)
            RefreshLocked();
    }

    void RefreshLocked()
    {
        OpenNewPacks();
        // The index is read once it exists, because re-reading its 600,000 entries on every refresh would stall.
        if (_hashes.Length == 0)
            _indexLoaded = false;
        foreach (var key in _loaded.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
            _loaded.Remove(key);
    }

    Tile? LoadTile(string name)
    {
        if (_loaded.TryGetValue(name, out var cached))
            return cached;
        if (FindTilePack(name) is not { } holder)
        {
            _loaded[name] = null;
            return null;
        }

        RecordReader reader = new(holder.Pack.GameData(TilePrefix + name)!);
        List<string> names = reader.List(1, r => r.String());
        var textures = new LoadedTexture?[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            PackTextureFiles.File? file = holder.Textures.Read(names[i]);
            if (file is null) continue;
            string fileName = $"{names[i]}_{file.Width}x{file.Height}_{file.Format}.bin";
            _files.Add(fileName, file.Bytes);
            textures[i] = _textures.Load(new SamplerBinding
            {
                Key = "bake0",
                Assigned = "bake0",
                Texture = names[i],
                File = fileName,
                Format = file.Format,
                Width = file.Width,
                Height = file.Height,
                WrapU = "Clamp",
                WrapV = "Clamp",
            });
        }

        var actors = new Dictionary<ulong, BakeActorData>();
        int count = reader.Count();
        for (int i = 0; i < count; i++)
        {
            ulong hash = (ulong)reader.U32() << 32 | reader.U32();
            reader.String();
            int original = reader.I32();
            List<BakeMaterial> materials = [];
            int n = reader.Count();
            for (int m = 0; m < n; m++)
            {
                string material = reader.String();
                int index = reader.I32(), texture = reader.I32();
                materials.Add(new BakeMaterial(material, index, texture, new Vector4(reader.F32(), reader.F32(), reader.F32(), reader.F32())));
            }

            actors[hash] = new BakeActorData(original, materials);
        }

        var tile = new Tile(textures, actors);
        _loaded[name] = tile;
        return tile;
    }

    public void Dispose()
    {
        _textures.Dispose();
        foreach ((AssetPack pack, PackTextureFiles files) in _packs)
        {
            files.Dispose();
            pack.Dispose();
        }
    }
}
