using System.Text.Json;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.TestBench;

/// <summary>Patches and compiles the merged-draw form of every G-buffer and z-only program the prepared models name, and says how many came out, were left to the ordinary draw, or failed.</summary>
static class MergedCheck
{
    public static int Run(GL gl, CacheLayout cache, int limit)
    {
        string shaders = Path.Combine(cache.Root, "_shaders");
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string manifest in Directory.EnumerateFiles(cache.Root, "*.manifest.json", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(manifest);
            if (text.Length == 0)
                continue;
            using var doc = JsonDocument.Parse(text);
            foreach (var shape in doc.RootElement.GetProperty("shapes").EnumerateArray())
                foreach (string key in new[] { "gbuffer_shader", "zonly_shader" })
                    if (shape.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } name)
                        names.Add(name);
        }

        var sources = new TotkShaderSources();
        int compiled = 0, ordinary = 0, failed = 0;
        var failures = new Dictionary<string, List<string>>();
        foreach (string name in names.Take(limit))
        {
            if (!File.Exists(Path.Combine(shaders, name + ".vert")) || !File.Exists(Path.Combine(shaders, name + ".frag")))
                continue;
            string vertex = sources.Clean(File.ReadAllText(Path.Combine(shaders, name + ".vert")));
            string fragment = sources.Clean(File.ReadAllText(Path.Combine(shaders, name + ".frag")));
            if (MergedShaderPatch.Apply(vertex, fragment) is not var (v, f))
            {
                ordinary++;
                continue;
            }
            try
            {
                gl.ReleaseProgram(GLProgramBuilder.Build(gl, v, f, name + "_merged"));
                compiled++;
            }
            catch (InvalidOperationException ex)
            {
                failed++;
                string first = ex.Message.Split('\n').Skip(1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? ex.Message;
                string kind = System.Text.RegularExpressions.Regex.Replace(first, @"\d+", "N");
                if (!failures.TryGetValue(kind, out var list))
                    failures[kind] = list = [];
                list.Add(name);
            }
        }

        Console.WriteLine($"merged programs: {compiled} compiled, {ordinary} left to the ordinary draw, {failed} failed, of {Math.Min(limit, names.Count)}");
        foreach (var (kind, list) in failures.OrderByDescending(p => p.Value.Count).Take(8))
            Console.WriteLine($"  {list.Count} x {kind}   e.g. {list[0]}");
        return failed == 0 ? 0 : 1;
    }
}
