using System.Collections.Concurrent;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;

namespace WildRenderingSharp.Shaders;

/// <summary>
/// Loads and links a decompiled vertex and fragment pair into a GL program, cached by base name so shapes and passes that use the same
/// program share it.
/// </summary>
internal sealed class ShaderProgramCache : IDisposable
{
    readonly GL _gl;
    readonly string _decompiledDir;
    readonly Dictionary<string, uint> _programs = new(StringComparer.Ordinal);

    public ShaderBindings Bindings { get; }

    readonly IShaderSources _sources;

    public ShaderProgramCache(GL gl, string decompiledDir, ShaderBindings bindings, IShaderSources sources)
    {
        _gl = gl;
        Bindings = bindings;
        _sources = sources;
        _decompiledDir = decompiledDir;
    }

    public bool Exists(string baseName) => File.Exists(Path.Combine(_decompiledDir, baseName + ".frag"));

    public uint Load(string baseName, bool isForwardProgram = false,
        Func<string, string>? patchVertex = null, Func<string, string>? patchFragment = null)
    {
        lock (_sync)
            return LoadLocked(baseName, isForwardProgram, patchVertex, patchFragment);
    }

    // Held for any use of the caches: a host may load models on a worker thread with a context of its own.
    readonly object _sync = new();

    // The version of the code that makes the sources these programs are built from: the cleaning, the corrections and the instanced and merged patches. A program kept by name is
    // good until this changes, so it is changed whenever any of them does. (Also the revision of the files themselves: a program is kept with their sizes and times.)
    const string SourceRevision = "1";

    // What the program's source files are now, for the identity a kept program is found by. Throws when they are missing, as reading them does.
    string Stamp(string baseName)
    {
        var vert = new FileInfo(Path.Combine(_decompiledDir, baseName + ".vert"));
        var frag = new FileInfo(Path.Combine(_decompiledDir, baseName + ".frag"));
        return $"{SourceRevision}|{baseName}|{vert.Length}:{vert.LastWriteTimeUtc.Ticks}|{frag.Length}:{frag.LastWriteTimeUtc.Ticks}";
    }

    uint LoadLocked(string baseName, bool isForwardProgram,
        Func<string, string>? patchVertex, Func<string, string>? patchFragment)
    {
        bool patched = patchVertex is not null || patchFragment is not null;
        if (!patched && _programs.TryGetValue(baseName, out uint cached))
            return cached;

        if (!patched)
        {
            uint kept = GLProgramBuilder.BuildNamed(_gl, $"plain|{isForwardProgram}|{Stamp(baseName)}", () =>
            {
                string vert = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
                string raw = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag")));
                return (vert, isForwardProgram ? _sources.CorrectForwardFragment(raw) : raw);
            }, baseName);
            _programs[baseName] = kept;
            return kept;
        }

        string vertSource = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
        string rawFragSource = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag")));
        string fragSource = isForwardProgram ? _sources.CorrectForwardFragment(rawFragSource) : rawFragSource;
        if (patchVertex is not null) vertSource = patchVertex(vertSource);
        if (patchFragment is not null) fragSource = patchFragment(fragSource);
        uint program = GLProgramBuilder.Build(_gl, vertSource, fragSource, baseName);
        if (!patched)
            _programs[baseName] = program;
        return program;
    }

    readonly ConcurrentDictionary<string, bool> _discards = new(StringComparer.Ordinal);

    // Whether a program's fragment stage can discard, read once from its source. An unreadable program counts as discarding. It takes no lock of the cache's, which a
    // link on another thread can hold for half a second.
    public bool FragmentDiscards(string baseName) => _discards.GetOrAdd(baseName, name =>
    {
        string path = Path.Combine(_decompiledDir, name + ".frag");
        return !File.Exists(path) || File.ReadAllText(path).Contains("discard", StringComparison.Ordinal);
    });

    readonly Dictionary<string, uint> _instancedPrograms = new(StringComparer.Ordinal);

    public uint LoadInstanced(string baseName, bool isForwardProgram = false)
    {
        lock (_sync)
            return LoadInstancedLocked(baseName, isForwardProgram);
    }

    uint LoadInstancedLocked(string baseName, bool isForwardProgram)
    {
        string key = (isForwardProgram ? "fwd:" : "") + baseName;
        if (_instancedPrograms.TryGetValue(key, out uint cached))
            return cached;

        uint keptProgram = GLProgramBuilder.BuildNamed(_gl, $"instanced|{isForwardProgram}|{_gl.SupportsBaseInstance()}|{Stamp(baseName)}", () =>
        {
            string vert = _sources.Instance(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert"))), _gl.SupportsBaseInstance()) ?? "";
            if (vert.Length == 0)
                return null;
            string raw = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag")));
            return (vert, isForwardProgram ? _sources.CorrectForwardFragment(raw) : raw);
        }, baseName + "_instanced");
        _instancedPrograms[key] = keptProgram;
        return keptProgram;
    }

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
                string v = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
                string f = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag")));
                return _sources.Merge(v, f);
            }, baseName + "_merged");
        }
        catch (InvalidOperationException ex)
        {
            WildRenderingSharp.Logging.Log.Warning($"[merged] {baseName} is drawn the ordinary way: {ex.Message.ReplaceLineEndings(" ")[..Math.Min(ex.Message.Length, 200)]}");
            return 0;
        }
    }

    public void Dispose()
    {
        foreach (var program in _mergedPrograms.Values)
            if (program.IsValueCreated && program.Value != 0)
                _gl.ReleaseProgram(program.Value);
        _mergedPrograms.Clear();
        foreach (uint program in _programs.Values)
            _gl.ReleaseProgram(program);
        _programs.Clear();
        foreach (uint program in _instancedPrograms.Values)
            if (program != 0)
                _gl.ReleaseProgram(program);
        _instancedPrograms.Clear();
    }
}
