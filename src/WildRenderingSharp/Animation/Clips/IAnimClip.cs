namespace WildRenderingSharp.Animation.Clips;

/// <summary>
/// A generic animation. Types loaded from BFRES are skeletal,
/// material parameter, and texture pattern animations.
/// </summary>
public interface IAnimClip
{
    string Name { get; }
    int FrameCount { get; }
    bool Loop { get; }
}
