using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred.PassIds;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>The deferred passes the loaded models (and a terrain host) need, resolved to compiled programs and material buffers.</summary>
internal sealed class DeferredScene(GL gl, ShaderProgramCache programs, AssetDirectories directories) : IDisposable
{
    // The pass that lights a host's terrain, and every pixel the pass-ID mask leaves unstamped.
    public const string DefaultPass = "field_hybrid";

    public const string WaterPass = "field_water";

    const string CharacterPassPrefix = "chara_";

    readonly List<string> _hostPasses = [];

    IReadOnlyList<LoadedModel> _models = [];
    List<string> _passNames = [];
    List<ResolvedDeferredPass> _resolvedPasses = [];
    IReadOnlyDictionary<int, string> _passByPriority = new Dictionary<int, string>();
    readonly Dictionary<string, IReadOnlyList<int>> _materialIds = new(StringComparer.Ordinal);

    public bool HasModels => _models.Count > 0;

    public IReadOnlyList<string> PassNames => _passNames;

    public IReadOnlyList<ResolvedDeferredPass> ResolvedPasses => _resolvedPasses;

    public bool NeedsKnownMaterialFixes { get; private set; }

    public int PassIndex(string pass) => _passNames.IndexOf(pass);

    // The pass that lights a shape. Its G-buffer program's own material IDs decide it, and the game lights ID 0 with the field pass, so a
    // shape named for a character pass whose program writes no ID is lit by the field pass, as every placed wall and cliff is.
    public string PassOf(LoadedShape shape)
    {
        string named = shape.DeferredPass();
        return named.StartsWith(CharacterPassPrefix, StringComparison.Ordinal) && MaterialIds(shape).Count == 0 ? DefaultPass : named;
    }

    public void Set(IReadOnlyList<LoadedModel> models)
    {
        _models = models;
        var shapes = models.SelectMany(m => m.Shapes).ToList();
        var passNames = shapes.Select(PassOf).Where(p => p.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        // The passes the shapes' own G-buffer programs say light them, which may not be the ones their materials are named for.
        _passByPriority = DeferredPassPriorities.ByPriority(directories.DeferredMaterials);
        foreach (int id in shapes.SelectMany(MaterialIds).Distinct())
            if (_passByPriority.TryGetValue(id, out string? written) && !passNames.Contains(written))
                passNames = [.. passNames, written];
        foreach (string hostPass in _hostPasses.Where(p => !passNames.Contains(p)))
            passNames = [.. passNames, hostPass];
        NeedsKnownMaterialFixes = shapes.Where(s => !s.Blend).Any(KnownMaterialFixes.NeedsEyeVisibilityMaskFix);

        // A shrine's entrance rock is a closed hull with a doorway in it, through which the hall reaches far into the ground behind; EntranceInterior draws the
        // rock, leaving it out of the doorway, the hall only there, and the doorway's fire over both. The rock's other part, a floor and ceiling at the doorway's
        // height, only cuts the ground out of the doorway in the game, which EntranceInterior does, and is drawn by nothing.
        foreach (LoadedShape shape in shapes)
        {
            bool hull = shape.IsEntranceRock() && shape.CullsFaces;
            shape.ProfileDrawsGBuffer = hull || shape.IsDoorwayPanel();
            shape.PortalOnly = shape.IsSeenThroughDoorway() || (shape.IsEntranceRock() && !hull);
        }

        // Resolving reads every pass's material off disk, so it only happens when the set of passes changes.
        if (passNames.SequenceEqual(_passNames, StringComparer.Ordinal) && _resolvedPasses.Count == passNames.Count)
            return;

        _passNames = passNames;
        DeleteMaterialBuffers();
        _resolvedPasses = DeferredPassLoader.Load(gl, programs, directories.Decompiled, directories.DeferredMaterials, _passNames);
        Log.Info($"  deferred passes: {string.Join(", ", _passNames)}");
    }

    // The priority and pass index of every resolved pass a G-buffer material ID can select.
    public IReadOnlyList<(int Priority, int PassIndex)> MaterialIdPasses()
    {
        var passes = new List<(int Priority, int PassIndex)>();
        foreach (var (priority, name) in _passByPriority)
        {
            int index = _passNames.IndexOf(name);
            if (index >= 0 && _resolvedPasses.Any(p => p.PassIndex == index))
                passes.Add((priority, index));
        }
        return passes;
    }

    IReadOnlyList<int> MaterialIds(LoadedShape shape)
    {
        if (!_materialIds.TryGetValue(shape.GBufferShaderName, out var ids))
        {
            string path = Path.Combine(directories.Decompiled, shape.GBufferShaderName + ".frag");
            ids = File.Exists(path) ? GBufferMaterialIds.Parse(File.ReadAllText(path)) : [];
            _materialIds[shape.GBufferShaderName] = ids;
        }
        return ids;
    }

    public void EnsurePass(string pass)
    {
        if (!_hostPasses.Contains(pass))
            _hostPasses.Add(pass);
        if (_passNames.Contains(pass))
            return;

        _passNames = [.. _passNames, pass];
        _resolvedPasses =
        [
            .. _resolvedPasses,
            .. DeferredPassLoader.Load(gl, programs, directories.Decompiled, directories.DeferredMaterials, [pass])
                .Select(p => p with { PassIndex = _passNames.Count - 1 }),
        ];
    }

    void DeleteMaterialBuffers()
    {
        foreach (var pass in _resolvedPasses)
            pass.Material.Dispose();
    }

    public void Dispose()
    {
        DeleteMaterialBuffers();
        _resolvedPasses = [];
    }
}
