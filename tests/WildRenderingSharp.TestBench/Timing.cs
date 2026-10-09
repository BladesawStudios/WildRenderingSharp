using System.Diagnostics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.TestBench;

/// <summary>Renders many frames and reports how long each took to submit on the CPU, how long the card took in all, and where the time went.</summary>
static class Timing
{
    const int Warmup = 30;

    public static void Report(WildRenderer renderer, GL gl, Camera camera, int size, int frames)
    {
        for (int i = 0; i < Warmup; i++)
            renderer.Render(camera, size, size, 1f / 60f);
        gl.Finish();

        var cpu = new double[frames];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            long start = Stopwatch.GetTimestamp();
            renderer.Render(camera, size, size, 1f / 60f);
            cpu[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        gl.Finish();
        total.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Array.Sort(cpu);
        Console.WriteLine($"frames: {frames} in {total.Elapsed.TotalMilliseconds:F0} ms = {total.Elapsed.TotalMilliseconds / frames:F2} ms/frame throughput; " +
            $"CPU submit median {cpu[frames / 2]:F2} ms, p95 {cpu[(int)(frames * 0.95)]:F2} ms; allocated {allocated / frames / 1024.0:F1} KB/frame");

        var timer = renderer.Pipeline.Timer;
        Console.WriteLine($"GPU {timer.LastTotalMs:F2} ms by pass: " + Top(timer.Last, 10));
        Console.WriteLine($"CPU {timer.LastCpu.Sum(p => p.Ms):F2} ms by pass: " + Top(timer.LastCpu, 10));
    }

    static string Top(IReadOnlyList<(string Pass, double Ms)> passes, int count) =>
        string.Join(", ", passes.OrderByDescending(p => p.Ms).Take(count).Select(p => $"{p.Pass} {p.Ms:F2}"));
}
