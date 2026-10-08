using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Loads and links a decompiled <c>&lt;base&gt;.vert</c>/<c>&lt;base&gt;.frag</c> pair from <c>Shaders/Decompiled</c> into a GL
/// program, caching by base name - many shapes/materials across a model (and across the deferred resolve passes) share the same
/// compiled program, so this is what makes that sharing actually happen instead of relinking per shape.
/// </summary>
public sealed class ShaderProgramCache : IDisposable
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

        string vertSource = _sources.Instance(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")))) ?? "";
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

    public void Dispose()
    {
        foreach (uint program in _programs.Values)
            _gl.DeleteProgram(program);
        _programs.Clear();
        ShapeDrawing.ForgetPrograms(_instancedPrograms.Values);
        foreach (uint program in _instancedPrograms.Values)
            if (program != 0)
                _gl.DeleteProgram(program);
        _instancedPrograms.Clear();
    }
}
