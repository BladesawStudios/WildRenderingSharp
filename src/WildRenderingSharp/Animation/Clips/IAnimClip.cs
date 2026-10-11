namespace WildRenderingSharp.Animation.Clips;

/// <summary>What every playable animation manifest has in common, so one playback clock and one browser UI can drive all four kinds.</summary>
public interface IAnimClip
{
    string Name { get; }
    int FrameCount { get; }
    bool Loop { get; }
}
