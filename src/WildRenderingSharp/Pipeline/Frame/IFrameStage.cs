namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>One step of a frame.</summary>
internal interface IFrameStage
{
    void Run(FrameContext frame);
}
