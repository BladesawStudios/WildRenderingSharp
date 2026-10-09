using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Profiles.Totk.Deferred.PassIds;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>The deferred passes the loaded models (and a terrain host) need, resolved to compiled programs and material buffers.</summary>
public sealed class DeferredScene(GL gl, ShaderProgramCache programs, AssetDirectories directories) : IDisposable
{
    /// <summary>The pass that lights a host's terrain, and every pixel the pass-ID mask leaves unstamped.</summary>
    public const string DefaultPass = "field_hybrid";

    public const string WaterPass = "field_water";

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

    public void Set(IReadOnlyList<LoadedModel> models)
    {
        _models = models;
        var shapes = models.SelectMany(m => m.Shapes).ToList();
        var passNames = PassIdMaskPass.DistinctPasses(shapes);
        // The passes the shapes' own G-buffer programs say light them, which may not be the ones their materials are named for.
        _passByPriority = DeferredPassPriorities.ByPriority(directories.DeferredMaterials);
        foreach (int id in shapes.SelectMany(MaterialIds).Distinct())
            if (_passByPriority.TryGetValue(id, out string? written) && !passNames.Contains(written))
                passNames = [.. passNames, written];
        foreach (string hostPass in _hostPasses.Where(p => !passNames.Contains(p)))
            passNames = [.. passNames, hostPass];
        NeedsKnownMaterialFixes = shapes.Where(s => !s.Blend).Any(KnownMaterialFixes.NeedsEyeVisibilityMaskFix);

        // Resolving reads every pass's material off disk, so it only happens when the set of passes changes.
        if (passNames.SequenceEqual(_passNames, StringComparer.Ordinal) && _resolvedPasses.Count == passNames.Count)
            return;

        _passNames = passNames;
        DeleteMaterialBuffers();
        _resolvedPasses = DeferredResolvePass.ResolveDeferredPasses(gl, programs, directories.Decompiled, directories.DeferredMaterials, _passNames);
        Console.WriteLine($"  deferred passes: {string.Join(", ", _passNames)}");
    }

    /// <summary>The priority and pass index of every resolved pass a G-buffer material ID can select.</summary>
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
            .. DeferredResolvePass.ResolveDeferredPasses(gl, programs, directories.Decompiled, directories.DeferredMaterials, [pass])
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
