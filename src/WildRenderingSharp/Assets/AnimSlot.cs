namespace WildRenderingSharp.Assets;

/// <summary>One clip and where it currently is: the frame, and whether it is running.</summary>
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

    public bool Stepped { get; }

    public float Frame { get; private set; }
    public bool Playing { get; set; }

    public string Name => Clip.Name;

    public void Seek(float frame)
    {
        float clamped = Math.Clamp(frame, 0f, Math.Max(0, Clip.FrameCount));
        Frame = Stepped ? MathF.Round(clamped) : clamped;
    }

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
