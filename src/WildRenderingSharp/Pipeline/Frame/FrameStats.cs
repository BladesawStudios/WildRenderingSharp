namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>How much a frame drew, for the host's statistics.</summary>
internal sealed class FrameStats
{
    public (long Triangles, long Instances) GBuffer { get; set; }

    public (long Triangles, long Instances) Shadow { get; set; }

    // Whether the frame composed a host's gloom into the G-buffer.
    public bool GloomComposed { get; set; }
}
