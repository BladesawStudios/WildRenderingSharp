namespace WildRenderingSharp.Debug;

/// <summary>Where a host that profiles hears about the renderer's named stretches of work.</summary>
/// <remarks>Begin and End are always paired and nest on the thread that calls them. A zone covers the CPU work between them and, for a host that times the GPU, the commands the GL thread submitted in that stretch.</remarks>
public interface IProfileSink
{
    void Begin(string name);

    void End();
}

/// <summary>Marks a named stretch of the frame for whatever profiler the host has plugged in.</summary>
/// <remarks>Costs one static read and a branch when no host has set Sink. Names should be literals or cached strings: a zone opened per draw with a freshly built name would allocate every time.</remarks>
public static class Probe
{
    public static IProfileSink? Sink { get; set; }

    // Opens a zone that ends when the returned value is disposed.
    public static Scope Zone(string name)
    {
        var sink = Sink;
        sink?.Begin(name);
        return new Scope(sink);
    }

    /// <summary>The open end of a zone, which closes it when disposed.</summary>
    /// <remarks>It holds only the sink that opened the zone, so a zone costs no allocation.</remarks>
    public readonly struct Scope(IProfileSink? sink) : IDisposable
    {
        public void Dispose() => sink?.End();
    }
}
