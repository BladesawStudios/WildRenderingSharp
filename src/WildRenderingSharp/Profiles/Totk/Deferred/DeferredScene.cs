using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>The deferred passes the loaded models (and a terrain host) need, resolved to compiled programs and material buffers.</summary>
public sealed class DeferredScene(GL gl, ShaderProgramCache programs, AssetDirectories directories) : IDisposable
{
    public const string DefaultPass = "chara_nonmetal";

    public const string WaterPass = "field_water";

    readonly List<string> _hostPasses = [];

    IReadOnlyList<LoadedModel> _models = [];
    List<string> _passNames = [];
    List<ResolvedDeferredPass> _resolvedPasses = [];

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
            gl.DeleteBuffer(pass.MaterialBuffer);
    }

    public void Dispose()
    {
        DeleteMaterialBuffers();
        _resolvedPasses = [];
    }
}
