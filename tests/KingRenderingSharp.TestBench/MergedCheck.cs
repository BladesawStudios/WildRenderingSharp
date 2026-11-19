using KingRenderingSharp.Gpu;
using KingRenderingSharp.Games.Totk.Shaders;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.TestBench;

/// <summary>Patches and compiles the merged-draw form of every G-buffer and z-only program the prepared models name, and says how many came out, were left to the ordinary draw, or failed.</summary>
static class MergedCheck
{
    public static int Run(GpuApi gl, CacheLayout cache, int limit)
    {
        // The programs of every model the cache has a pack for, by the names its shapes give them.
        var packs = new List<KingRenderingSharp.Content.Packs.ModelPack>();
        var names = new SortedDictionary<string, KingRenderingSharp.Content.Packs.ModelPack>(StringComparer.Ordinal);
        foreach (string model in KingRenderingSharp.Content.Assets.Manifests.ModelManifest.ListAvailableModels(cache.Root))
        {
            var pack = new KingRenderingSharp.Content.Packs.ModelPack(cache.ModelPack(model), cache.ModelShaders(model), model);
            packs.Add(pack);
            foreach (var shape in pack.Source.Manifest.Shapes)
                foreach (string name in new[] { shape.GBufferShader, shape.ZOnlyShader })
                    if (name.Length > 0) names.TryAdd(name, pack);
        }

        var sources = new TotkShaderSources();
        int compiled = 0, ordinary = 0, failed = 0;
        var failures = new Dictionary<string, List<string>>();
        foreach (string name in names.Keys.Take(limit))
        {
            var programs = names[name].Programs;
            if (!programs.Exists(name + ".vert") || !programs.Exists(name + ".frag"))
                continue;
            string vertex = sources.Clean(programs.ReadText(name + ".vert"));
            string fragment = sources.Clean(programs.ReadText(name + ".frag"));
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
        foreach (var pack in packs) pack.Dispose();
        return failed == 0 ? 0 : 1;
    }
}
