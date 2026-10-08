namespace WildRenderingSharp.Assets;

/// <summary>
/// Every clip of one kind that is currently applied, each on its own clock.
///
/// Several clips of the same kind genuinely do run together. Enemy_Dragon_Darkness has four
/// separate <c>Weakness_0N_Death_ftp</c> anims, one per weak-point material, and showing more than
/// one broken weak point means running more than one of them; likewise its shader parameter anims
/// split across face, body and luminance, authored to be combined. The engine binds each animation
/// resource independently for exactly this reason, so a channel is a SET, not a single selection.
///
/// Order matters where two clips write the same thing: they are applied in the order added, so a
/// later one wins, matching the engine's own sequential ApplyTo.
/// </summary>
public sealed class AnimChannel<T> where T : class, IAnimClip
{
    readonly List<AnimSlot<T>> _slots = [];

    /// <param name="stepped">Whether clips in this channel are dragged between whole frames rather than played - see <see cref="AnimSlot{T}.Stepped"/>.</param>
    public AnimChannel(bool stepped = false) => Stepped = stepped;

    public bool Stepped { get; }

    public IReadOnlyList<AnimSlot<T>> Slots => _slots;
    public int Count => _slots.Count;
    public bool IsActive => _slots.Count > 0;

    public bool Contains(string name) => _slots.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    /// <summary>Adds a clip if it isn't already applied; returns its slot either way.</summary>
    public AnimSlot<T> Add(T clip)
    {
        var existing = _slots.FirstOrDefault(s => string.Equals(s.Name, clip.Name, StringComparison.Ordinal));
        if (existing is not null)
            return existing;
        var slot = new AnimSlot<T>(clip, Stepped);
        _slots.Add(slot);
        return slot;
    }

    public void Remove(string name) => _slots.RemoveAll(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    public void Clear() => _slots.Clear();

    /// <summary>Advances every playing slot; true if any moved.</summary>
    public bool Advance(float deltaSeconds, float framesPerSecond)
    {
        bool moved = false;
        foreach (var slot in _slots)
            moved |= slot.Advance(deltaSeconds, framesPerSecond);
        return moved;
    }
}
