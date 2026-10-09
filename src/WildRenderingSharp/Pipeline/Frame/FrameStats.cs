namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>How much a frame drew, for the host's statistics.</summary>
internal sealed class FrameStats
{
    public (long Triangles, long Instances) GBuffer { get; set; }

    public (long Triangles, long Instances) Shadow { get; set; }
}
