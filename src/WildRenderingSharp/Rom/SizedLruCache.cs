namespace WildRenderingSharp.Rom;

/// <summary>
/// A cache that drops its least recently used entries once their sizes add up to more than a budget. The newest entry is always kept,
/// so one entry larger than the budget still caches.
/// </summary>
public sealed class SizedLruCache<T>(long budget, Func<T, long> sizeOf) where T : class
{
    readonly object _gate = new();
    readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.OrdinalIgnoreCase);
    readonly LinkedList<Entry> _recentFirst = new();
    long _total;

    public long TotalSize
    {
        get
        {
            lock (_gate)
                return _total;
        }
    }

    public bool TryGet(string key, out T value)
    {
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out var node))
            {
                value = null!;
                return false;
            }
            _recentFirst.Remove(node);
            _recentFirst.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    /// <summary>Stores <paramref name="value"/> unless the key is already held, and returns the value the cache holds.</summary>
    public T Add(string key, T value)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var existing))
                return existing.Value.Value;

            long size = sizeOf(value);
            _byKey[key] = _recentFirst.AddFirst(new Entry(key, value, size));
            _total += size;
            while (_total > budget && _recentFirst.Count > 1)
            {
                var oldest = _recentFirst.Last!.Value;
                _recentFirst.RemoveLast();
                _byKey.Remove(oldest.Key);
                _total -= oldest.Size;
            }
            return value;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _byKey.Clear();
            _recentFirst.Clear();
            _total = 0;
        }
    }

    readonly record struct Entry(string Key, T Value, long Size);
}
