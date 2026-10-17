namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>Hands out runs of consecutive elements from a range of them and takes them back, joining runs that touch; it holds no memory of its own.</summary>
internal sealed class RegionAllocator(int capacity)
{
    // Free runs, by start, which never touch one another.
    readonly SortedList<int, int> _free = new() { { 0, capacity } };

    public int Capacity { get; private set; } = capacity;

    public int Used { get; private set; }

    // The start of a run of count elements, or -1 when no free run is that long.
    public int Allocate(int count)
    {
        foreach (var (start, length) in _free)
        {
            if (length < count)
                continue;
            _free.Remove(start);
            if (length > count)
                _free.Add(start + count, length - count);
            Used += count;
            return start;
        }
        return -1;
    }

    public void Free(int start, int count)
    {
        Used -= count;
        int end = start + count;
        if (_free.Count > 0)
        {
            int before = _free.IndexOfKey(Predecessor(start));
            if (before >= 0 && _free.Keys[before] + _free.Values[before] == start)
            {
                start = _free.Keys[before];
                count += _free.Values[before];
                _free.RemoveAt(before);
            }
        }
        if (_free.TryGetValue(end, out int after))
        {
            _free.Remove(end);
            count += after;
        }
        _free.Add(start, count);
    }

    // Makes the range longer, the new elements free.
    public void Grow(int newCapacity)
    {
        int added = newCapacity - Capacity;
        Capacity = newCapacity;
        int tailStart = Capacity - added;
        if (_free.Count > 0 && _free.Keys[^1] + _free.Values[^1] == tailStart)
            _free[_free.Keys[^1]] += added;
        else
            _free.Add(tailStart, added);
    }

    // The start of the free run that lies last before the position, or -1.
    int Predecessor(int position)
    {
        int found = -1;
        foreach (int start in _free.Keys)
        {
            if (start >= position)
                break;
            found = start;
        }
        return found;
    }
}
