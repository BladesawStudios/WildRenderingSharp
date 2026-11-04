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
    readonly ConcurrentDictionary<string, Lazy<uint>> _programs = new(StringComparer.Ordinal);

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
        var assembly = typeof(ShaderProgramCache).Assembly;
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var resources = assembly.GetManifestResourceNames().ToDictionary(n => n.Replace(Path.DirectorySeparatorChar, '/'));
        foreach (var (name, resource) in resources.OrderBy(r => r.Key, StringComparer.Ordinal))
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
        var vert = new FileInfo(Path.Combine(_decompiledDir, baseName + ".vert"));
        var frag = new FileInfo(Path.Combine(_decompiledDir, baseName + ".frag"));
        return $"{SourceRevision}|{baseName}|{vert.Length}:{vert.LastWriteTimeUtc.Ticks}|{frag.Length}:{frag.LastWriteTimeUtc.Ticks}";
    }

    uint BuildPlain(string baseName, bool isForwardProgram) =>
        GLProgramBuilder.BuildNamed(_gl, $"plain|{isForwardProgram}|{Stamp(baseName)}", () =>
        {
            string vert = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
            string raw = UnwrittenOutputs.Apply(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag"))));
            return (vert, isForwardProgram ? _sources.CorrectForwardFragment(raw) : raw);
        }, baseName);

    uint LoadPatched(string baseName, bool isForwardProgram,
        Func<string, string>? patchVertex, Func<string, string>? patchFragment)
    {
        string vertSource = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
        string rawFragSource = UnwrittenOutputs.Apply(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag"))));
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
        string path = Path.Combine(_decompiledDir, name + ".frag");
        return !File.Exists(path) || File.ReadAllText(path).Contains("discard", StringComparison.Ordinal);
    });

    readonly ConcurrentDictionary<string, Lazy<uint>> _instancedPrograms = new(StringComparer.Ordinal);

    public uint LoadInstanced(string baseName, bool isForwardProgram = false) =>
        Once(_instancedPrograms, (isForwardProgram ? "fwd:" : "") + baseName, () => BuildInstanced(baseName, isForwardProgram));

    uint BuildInstanced(string baseName, bool isForwardProgram) =>
        GLProgramBuilder.BuildNamed(_gl, $"instanced|{isForwardProgram}|{_gl.SupportsBaseInstance()}|{Stamp(baseName)}", () =>
        {
            string vert = _sources.Instance(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert"))), _gl.SupportsBaseInstance()) ?? "";
            if (vert.Length == 0)
                return null;
            string raw = UnwrittenOutputs.Apply(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag"))));
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
                string v = _sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".vert")));
                string f = UnwrittenOutputs.Apply(_sources.Clean(File.ReadAllText(Path.Combine(_decompiledDir, baseName + ".frag"))));
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
