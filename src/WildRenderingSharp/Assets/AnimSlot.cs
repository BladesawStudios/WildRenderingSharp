namespace WildRenderingSharp.Assets;

/// <summary>
/// One clip and where it currently is: the frame, and whether it is running. A STEPPED slot (see <see cref="Stepped"/>) has no
/// meaningful in-between: a texture pattern anim selects a texture by integer index, so frame 2.5 is not "half way between textures
/// 2 and 3", it is just frame 2 with a misleading label. Those slots keep <see cref="Frame"/> on whole numbers and never play on
/// their own.
/// </summary>
public sealed class AnimSlot<T> where T : class, IAnimClip
{
    public AnimSlot(T clip, bool stepped)
    {
        Clip = clip;
        Stepped = stepped;
        // A one-frame clip is a plain "use this instead" swap, not something to loop at 30 Hz.
        Playing = !stepped && clip.FrameCount > 1;
    }

    public T Clip { get; }

    /// <summary>Frames are whole numbers and this slot does not advance by itself - it is dragged.</summary>
    public bool Stepped { get; }

    public float Frame { get; private set; }
    public bool Playing { get; set; }

    public string Name => Clip.Name;

    /// <summary>Seeks. A stepped slot snaps to a whole frame, so it can never sit between two texture selections.</summary>
    public void Seek(float frame)
    {
        float clamped = Math.Clamp(frame, 0f, Math.Max(0, Clip.FrameCount));
        Frame = Stepped ? MathF.Round(clamped) : clamped;
    }

    /// <summary>
    /// Advances by real time if playing, looping or clamping to the clip's own frame count. Returns true if the frame moved, so the
    /// caller knows to redraw.
    /// </summary>
    public bool Advance(float deltaSeconds, float framesPerSecond)
    {
        if (Stepped || !Playing || Clip.FrameCount <= 0)
            return false;
        float frame = Frame + deltaSeconds * framesPerSecond;
        if (frame > Clip.FrameCount)
            frame = Clip.Loop ? frame % Clip.FrameCount : Clip.FrameCount;
        Frame = frame;
        return true;
    }
}
