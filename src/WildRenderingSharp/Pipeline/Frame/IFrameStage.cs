namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>One step of a frame. Stages run in order against the same <see cref="FrameContext"/>.</summary>
public interface IFrameStage
{
    void Run(FrameContext frame);
}
