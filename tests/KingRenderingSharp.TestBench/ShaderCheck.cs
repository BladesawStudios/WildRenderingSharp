using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Graphics.Contracts;
using KingRenderingSharp.Games.Botw.Shaders;
using KingRenderingSharp.Games.Totk.Shaders;

namespace KingRenderingSharp.TestBench;

/// <summary>Compiles and links every extracted program of a shader folder as the renderer cleans it, and says how many build and why the others do not.</summary>
/// <remarks>The folder holds <c>name.vert</c> and <c>name.frag</c> pairs; each source goes through the game's cleaning pass first, as it does on load, so a failure here is a failure the renderer would have.</remarks>
static class ShaderCheck
{
    public static int RunHeadless(string folder, int limit, bool botw)
    {
        var window = Silk.NET.Windowing.Window.Create(Silk.NET.Windowing.WindowOptions.Default with
        {
            IsVisible = false,
            Size = new(64, 64),
            API = new Silk.NET.Windowing.GraphicsAPI(Silk.NET.Windowing.ContextAPI.OpenGL, Silk.NET.Windowing.ContextProfile.Core, Silk.NET.Windowing.ContextFlags.ForwardCompatible, new Silk.NET.Windowing.APIVersion(4, 5)),
        });
        window.Initialize();
        using var gl = GpuApi.Create(window.GLContext);
        return Run(gl, folder, limit, botw);
    }

    public static int Run(GpuApi gl, string folder, int limit, bool botw)
    {
        IShaderSources sources = botw ? new BotwShaderSources() : new TotkShaderSources();
        string[] names = [.. Directory.EnumerateFiles(folder, "*.frag").Select(f => Path.GetFileNameWithoutExtension(f)).Where(n => File.Exists(Path.Combine(folder, n + ".vert"))).Order(StringComparer.Ordinal)];
        if (limit > 0) names = [.. names.Take(limit)];
        int ok = 0;
        Dictionary<string, int> reasons = [];
        List<string> failed = [];
        foreach (string name in names)
        {
            string error = Build(gl, sources.Clean(File.ReadAllText(Path.Combine(folder, name + ".vert"))), sources.CorrectForwardFragment(sources.Clean(File.ReadAllText(Path.Combine(folder, name + ".frag")))));
            if (error.Length == 0) { ok++; continue; }
            failed.Add(name);
            if (Environment.GetEnvironmentVariable("KRS_SHADER_VERBOSE") == "1") Console.WriteLine($"{name}: {error}");
            string key = error.Split('\n')[0].Trim();
            key = System.Text.RegularExpressions.Regex.Replace(key, @"\d+", "N");
            reasons[key] = reasons.GetValueOrDefault(key) + 1;
        }

        Console.WriteLine($"{ok} of {names.Length} programs in {folder} build");
        foreach (var reason in reasons.OrderByDescending(r => r.Value).Take(15)) Console.WriteLine($"  {reason.Value,5}  {reason.Key}");
        foreach (string name in failed.Take(10)) Console.WriteLine($"  failed: {name}");
        return failed.Count == 0 ? 0 : 1;
    }

    static string Build(GpuApi gl, string vertex, string fragment)
    {
        uint program = gl.CreateProgram();
        uint[] shaders = [Compile(gl, ShaderType.VertexShader, vertex, out string vlog), Compile(gl, ShaderType.FragmentShader, fragment, out string flog)];
        try
        {
            if (vlog.Length > 0) return "vertex: " + vlog;
            if (flog.Length > 0) return "fragment: " + flog;
            foreach (uint shader in shaders) gl.AttachShader(program, shader);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            return linked != 0 ? "" : "link: " + gl.GetProgramInfoLog(program);
        }
        finally
        {
            gl.DeleteProgram(program);
            foreach (uint shader in shaders) gl.DeleteShader(shader);
        }
    }

    static uint Compile(GpuApi gl, ShaderType type, string source, out string log)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        log = compiled != 0 ? "" : gl.GetShaderInfoLog(shader);
        return shader;
    }
}
