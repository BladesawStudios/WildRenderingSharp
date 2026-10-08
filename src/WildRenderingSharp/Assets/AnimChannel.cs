namespace WildRenderingSharp.Assets;

/// <summary>Every clip of one kind that is currently applied, each on its own clock.</summary>
public sealed class AnimChannel<T> where T : class, IAnimClip
{
    readonly List<AnimSlot<T>> _slots = [];

    public AnimChannel(bool stepped = false) => Stepped = stepped;

    public bool Stepped { get; }

    public IReadOnlyList<AnimSlot<T>> Slots => _slots;
    public int Count => _slots.Count;
    public bool IsActive => _slots.Count > 0;

    public bool Contains(string name) => _slots.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));

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

    public bool Advance(float deltaSeconds, float framesPerSecond)
    {
        bool moved = false;
        foreach (var slot in _slots)
            moved |= slot.Advance(deltaSeconds, framesPerSecond);
        return moved;
    }
}
