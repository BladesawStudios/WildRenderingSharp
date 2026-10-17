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

    uint LoadLocked(string baseName, bool isForwardProgram,
        Func<string, string>? patchVertex, Func<string, string>? patchFragment)
    {
        bool patched = patchVertex is not null || patchFragment is not null;
        if (!patched && _programs.TryGetValue(baseName, out uint cached))
            return cached;

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

    readonly Dictionary<string, bool> _discards = new(StringComparer.Ordinal);

    // Whether a program's fragment stage can discard, read once from its source. An unreadable program counts as discarding.
    public bool FragmentDiscards(string baseName)
    {
        lock (_sync)
        {
            if (!_discards.TryGetValue(baseName, out bool discards))
            {
                string path = Path.Combine(_decompiledDir, baseName + ".frag");
                discards = !File.Exists(path) || File.ReadAllText(path).Contains("discard", StringComparison.Ordinal);
                _discards[baseName] = discards;
            }
            return discards;
        }
    }

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

        string vertSource = _sources.Instance(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert"))), _gl.SupportsBaseInstance()) ?? "";
        uint program = 0;
        if (vertSource.Length > 0)
        {
            string rawFragSource = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag")));
            string fragSource = isForwardProgram ? _sources.CorrectForwardFragment(rawFragSource) : rawFragSource;
            program = GLProgramBuilder.Build(_gl, vertSource, fragSource, baseName + "_instanced");
        }
        _instancedPrograms[key] = program;
        return program;
    }

    readonly Dictionary<string, uint> _mergedPrograms = new(StringComparer.Ordinal);

    // The program that draws any shape of any batch from one multi-draw, or 0 when this one cannot be (or did not compile) and is drawn the ordinary way.
    public uint LoadMerged(string baseName)
    {
        lock (_sync)
        {
            if (_mergedPrograms.TryGetValue(baseName, out uint cached))
                return cached;

            string vertex = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
            string fragment = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag")));
            uint program = 0;
            if (_sources.Merge(vertex, fragment) is var (mergedVertex, mergedFragment))
            {
                try
                {
                    program = GLProgramBuilder.Build(_gl, mergedVertex, mergedFragment, baseName + "_merged");
                }
                catch (InvalidOperationException ex)
                {
                    WildRenderingSharp.Logging.Log.Warning($"[merged] {baseName} is drawn the ordinary way: {ex.Message.ReplaceLineEndings(" ")[..Math.Min(ex.Message.Length, 200)]}");
                }
            }
            return _mergedPrograms[baseName] = program;
        }
    }

    public void Dispose()
    {
        foreach (uint program in _mergedPrograms.Values)
            if (program != 0)
                _gl.ReleaseProgram(program);
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
