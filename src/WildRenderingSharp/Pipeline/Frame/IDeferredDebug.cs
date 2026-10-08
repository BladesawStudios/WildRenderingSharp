namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>What a deferred frame graph offers a host that wants to look inside it.</summary>
public interface IDeferredDebug
{
    IReadOnlyList<string> PassNames { get; }

    /// <summary>The pass (index into <see cref="PassNames"/>) whose own output is kept for a debug view, or -1.</summary>
    int DebugResolvePass { get; set; }

    /// <summary>Writes every value <paramref name="pass"/> computes for the middle pixel of the next frame to <paramref name="path"/>.</summary>
    void TraceResolvePass(string pass, string path);
}
