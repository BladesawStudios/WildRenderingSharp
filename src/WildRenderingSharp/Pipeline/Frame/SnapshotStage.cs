namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Keeps a copy of the HDR frame as it stands, for the scene view's between-passes modes, when the host asked for them.</summary>
public sealed class SnapshotStage(int slot) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        if (frame.SnapshotStages)
            frame.Targets.SnapshotFinal(slot);
    }
}
