namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>What a deferred frame graph offers a host that wants to look inside it.</summary>
internal interface IDeferredDebug
{
    IReadOnlyList<string> PassNames { get; }

    // The pass (index into PassNames) whose own output is kept for a debug view, or -1.
    int DebugResolvePass { get; set; }

    // Writes every value pass computes for the middle pixel of the next frame to path.
    void TraceResolvePass(string pass, string path);
}
