namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>One step of a frame.</summary>
public interface IFrameStage
{
    void Run(FrameContext frame);
}
