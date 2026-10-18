using System.Diagnostics;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Merged;

namespace WildRenderingSharp.Debug;

/// <summary>
/// TEMPORARY. With WRS_LOG_RESIDENCY=1, every five seconds says how much of what the renderer holds on the card belongs to models drawn this frame, to models that
/// only cast into the shadow maps, and to models nothing draws, so what to stop holding can be read from the log.
/// </summary>
internal static class ResidencyLog
{
    static readonly bool On = Environment.GetEnvironmentVariable("WRS_LOG_RESIDENCY") == "1";
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Maybe(IReadOnlyList<InstanceBatch> batches)
    {
        if (!On || batches.Count == 0 || Clock.Elapsed.TotalSeconds < 5)
            return;
        Clock.Restart();

        // 0 drawn, 1 shadow only, 2 idle
        var models = new int[3];
        var shapes = new int[3];
        var own = new long[3];
        var arena = new long[3];
        var instances = new long[3];
        var textureBytes = new long[3];
        var textureCount = new int[3];
        var seen = new Dictionary<uint, int>();
        var idle = new List<(string Name, long Bytes, int Count)>();

        foreach (var batch in batches)
        {
            int kind = batch.Visible.Count > 0 ? 0 : HasShadowRuns(batch) ? 1 : 2;
            models[kind]++;
            instances[kind] += batch.Count;
            long bytes = 0;
            foreach (var shape in batch.Model.Shapes)
            {
                shapes[kind]++;
                long mine = shape.Demoted || shape.Buffers is null ? 0 : shape.Buffers.VertexBytes + shape.Buffers.IndexBytes;
                long slice = shape.Merged is MergedShape m
                    ? (long)m.Geometry.VertexCount * m.Geometry.Chunk.Stride + (long)m.Geometry.IndexCount * sizeof(uint)
                    : 0;
                own[kind] += mine;
                arena[kind] += slice;
                bytes += mine + slice;
                foreach (var sampler in shape.GBufferSamplers.Concat(shape.ZOnlySamplers).Concat(shape.ForwardSamplers))
                {
                    var t = sampler.Texture;
                    if (seen.TryGetValue(t.Handle, out int was) && was <= kind)
                        continue;
                    if (was > kind || !seen.ContainsKey(t.Handle))
                    {
                        if (seen.ContainsKey(t.Handle))
                        {
                            textureBytes[was] -= t.Bytes;
                            textureCount[was]--;
                        }
                        seen[t.Handle] = kind;
                        textureBytes[kind] += t.Bytes;
                        textureCount[kind]++;
                    }
                }
            }
            if (kind == 2)
                idle.Add((Path.GetFileName(batch.Model.DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), bytes, batch.Count));
        }

        static string Mb(long bytes) => $"{bytes / 1048576.0:F0} MB";
        string[] names = ["drawn this frame", "shadow only", "idle"];
        for (int k = 0; k < 3; k++)
            Log.Info($"[residency] {names[k]}: {models[k]:N0} models, {shapes[k]:N0} shapes, {instances[k]:N0} placements; own buffers {Mb(own[k])}, in the arena {Mb(arena[k])}, textures {Mb(textureBytes[k])} ({textureCount[k]:N0})");
        Log.Info("[residency] biggest idle: " + string.Join(", ", idle.OrderByDescending(i => i.Bytes).Take(8).Select(i => $"{i.Name} {Mb(i.Bytes)} x{i.Count}")));
    }

    static bool HasShadowRuns(InstanceBatch batch)
    {
        if (batch.Shadow.Visible.Count > 0)
            return true;
        for (int c = 0; c < 4; c++)
            if (batch.Shadow.Cascade(c).Count > 0)
                return true;
        return false;
    }
}
