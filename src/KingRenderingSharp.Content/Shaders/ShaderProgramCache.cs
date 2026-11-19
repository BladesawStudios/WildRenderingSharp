using System.Collections.Concurrent;
using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Graphics.Contracts;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Shaders;

/// <summary>
/// Loads and links a decompiled vertex and fragment pair into a GL program, cached by base name so shapes and passes that use the same
/// program share it.
/// </summary>
internal sealed class ShaderProgramCache : IDisposable
{
    readonly GpuApi _gl;
    readonly IContentFiles _files;
    readonly ConcurrentDictionary<string, Lazy<uint>> _programs = new(StringComparer.Ordinal);

    public ShaderBindings Bindings { get; }

    readonly IShaderSources _sources;

    public ShaderProgramCache(GpuApi gl, IContentFiles files, ShaderBindings bindings, IShaderSources sources)
    {
        _gl = gl;
        Bindings = bindings;
        _sources = sources;
        _files = files;
    }

    public bool Exists(string baseName) => _files.Exists(baseName + ".frag");

    public uint Load(string baseName, bool isForwardProgram = false,
        Func<string, string>? patchVertex = null, Func<string, string>? patchFragment = null)
    {
        if (patchVertex is not null || patchFragment is not null)
        {
            lock (_sync)
                return LoadPatched(baseName, isForwardProgram, patchVertex, patchFragment);
        }
        return Once(_programs, baseName, () => BuildPlain(baseName, isForwardProgram));
    }

    // Held while a program is made from patched sources, which are not kept: a host may load models on worker threads with contexts of their own.
    readonly object _sync = new();

    // The program kept under key, made by whichever thread asks first while the others who want that one wait for it and nobody else waits. A failed build is made again the next time.
    static uint Once(ConcurrentDictionary<string, Lazy<uint>> kept, string key, Func<uint> make)
    {
        var lazy = kept.GetOrAdd(key, _ => new Lazy<uint>(make));
        try
        {
            return lazy.Value;
        }
        catch
        {
            kept.TryRemove(new KeyValuePair<string, Lazy<uint>>(key, lazy));
            throw;
        }
    }

    // The version of the code that makes the sources these programs are built from: the cleaning, the corrections and the instanced and merged patches. A program kept by name is
    // good until this changes, so it is read off the code itself - the patch sources and the GLSL they insert, embedded in the build - and not left to be remembered. (Also the
    // revision of the files themselves: a program is kept with their sizes and times.)
    static readonly string SourceRevision = PatchRevision();

    static string PatchRevision()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var resources = LayerResources.Named()
            .Select(r => (r.Assembly, Resource: r.Name, Name: r.Name.Replace(Path.DirectorySeparatorChar, '/')))
            .OrderBy(r => r.Name, StringComparer.Ordinal);
        foreach (var (assembly, resource, name) in resources)
        {
            if (!name.StartsWith("Patch/", StringComparison.Ordinal) && !name.Contains("/Shaders/", StringComparison.Ordinal))
                continue;
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(name));
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
                continue;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            hash.AppendData(memory.ToArray());
        }
        return "3" + Convert.ToHexString(hash.GetHashAndReset(), 0, 8);
    }

    // What the program's source files are now, for the identity a kept program is found by. Throws when they are missing, as reading them does.
    string Stamp(string baseName)
    {
        return $"{SourceRevision}|{baseName}|{_files.Stamp(baseName + ".vert")}|{_files.Stamp(baseName + ".frag")}";
    }

    uint BuildPlain(string baseName, bool isForwardProgram) =>
        GLProgramBuilder.BuildNamed(_gl, $"plain|{isForwardProgram}|{Stamp(baseName)}", () =>
        {
            string vert = _sources.Clean(_files.ReadText(baseName + ".vert"));
            string raw = UnwrittenOutputs.Apply(_sources.Clean(_files.ReadText(baseName + ".frag")));
            return (vert, isForwardProgram ? _sources.CorrectForwardFragment(raw) : raw);
        }, baseName);

    uint LoadPatched(string baseName, bool isForwardProgram,
        Func<string, string>? patchVertex, Func<string, string>? patchFragment)
    {
        string vertSource = _sources.Clean(_files.ReadText(baseName + ".vert"));
        string rawFragSource = UnwrittenOutputs.Apply(_sources.Clean(_files.ReadText(baseName + ".frag")));
        string fragSource = isForwardProgram ? _sources.CorrectForwardFragment(rawFragSource) : rawFragSource;
        if (patchVertex is not null) vertSource = patchVertex(vertSource);
        if (patchFragment is not null) fragSource = patchFragment(fragSource);
        return GLProgramBuilder.Build(_gl, vertSource, fragSource, baseName);
    }

    readonly ConcurrentDictionary<string, bool> _discards = new(StringComparer.Ordinal);

    // Whether a program's fragment stage can discard, read once from its source. An unreadable program counts as discarding. It takes no lock of the cache's, which a
    // link on another thread can hold for half a second.
    public bool FragmentDiscards(string baseName) => _discards.GetOrAdd(baseName, name =>
    {
        return !_files.Exists(name + ".frag") || _files.ReadText(name + ".frag").Contains("discard", StringComparison.Ordinal);
    });

    readonly ConcurrentDictionary<string, Lazy<uint>> _instancedPrograms = new(StringComparer.Ordinal);

    public uint LoadInstanced(string baseName, bool isForwardProgram = false) =>
        Once(_instancedPrograms, (isForwardProgram ? "fwd:" : "") + baseName, () => BuildInstanced(baseName, isForwardProgram));

    uint BuildInstanced(string baseName, bool isForwardProgram) =>
        GLProgramBuilder.BuildNamed(_gl, $"instanced|{isForwardProgram}|{_gl.SupportsBaseInstance()}|{Stamp(baseName)}", () =>
        {
            string vert = _sources.Instance(_sources.Clean(_files.ReadText(baseName + ".vert")), _gl.SupportsBaseInstance()) ?? "";
            if (vert.Length == 0)
                return null;
            string raw = UnwrittenOutputs.Apply(_sources.Clean(_files.ReadText(baseName + ".frag")));
            return (vert, isForwardProgram ? _sources.CorrectForwardFragment(raw) : raw);
        }, baseName + "_instanced");

    readonly ConcurrentDictionary<string, Lazy<uint>> _mergedPrograms = new(StringComparer.Ordinal);

    // The program that draws any shape of any batch from one multi-draw, or 0 when this one cannot be (or did not compile) and is drawn the ordinary way. Each is
    // linked once, by whichever thread asks first, and the cache's lock is not held while it is, so asking for one already linked never waits on another's link.
    public uint LoadMerged(string baseName) => _mergedPrograms.GetOrAdd(baseName, name => new Lazy<uint>(() => BuildMerged(name))).Value;

    uint BuildMerged(string baseName)
    {
        try
        {
            return GLProgramBuilder.BuildNamed(_gl, $"merged|{Stamp(baseName)}", () =>
            {
                string v = _sources.Clean(_files.ReadText(baseName + ".vert"));
                string f = UnwrittenOutputs.Apply(_sources.Clean(_files.ReadText(baseName + ".frag")));
                return _sources.Merge(v, f);
            }, baseName + "_merged");
        }
        catch (InvalidOperationException ex)
        {
            KingRenderingSharp.Core.Logging.Log.Warning($"[merged] {baseName} is drawn the ordinary way: {ex.Message.ReplaceLineEndings(" ")[..Math.Min(ex.Message.Length, 200)]}");
            return 0;
        }
    }

    public void Dispose()
    {
        foreach (var program in _mergedPrograms.Values)
            if (program.IsValueCreated && program.Value != 0)
                _gl.ReleaseProgram(program.Value);
        _mergedPrograms.Clear();
        foreach (var program in _programs.Values)
            if (program.IsValueCreated)
                _gl.ReleaseProgram(program.Value);
        _programs.Clear();
        foreach (var program in _instancedPrograms.Values)
            if (program.IsValueCreated && program.Value != 0)
                _gl.ReleaseProgram(program.Value);
        _instancedPrograms.Clear();
    }
}
