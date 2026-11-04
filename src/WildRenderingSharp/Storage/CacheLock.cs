namespace WildRenderingSharp.Storage;

/// <summary>A lock on a piece of the cache that only one process should be making at a time, held through a file so it works between processes.</summary>
/// <remarks>
/// Several preparer processes may start at once, and each begins by making the cache's system assets if they are not current. Without this they all find them not current and
/// all write the same files together. With it the first makes them and the rest wait, then find them current and do nothing. The operating system releases the file when the
/// process holding it ends, however it ends, so a crashed preparer never leaves the lock held.
/// </remarks>
public sealed class CacheLock : IDisposable
{
    readonly FileStream _held;

    CacheLock(FileStream held) => _held = held;

    // Waits until nobody else holds the lock called name in the cache, then holds it until disposed.
    public static CacheLock Acquire(CacheLayout cache, string name, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cache.Root);
        string path = Path.Combine(cache.Root, $"_{name}.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new CacheLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException)
            {
                // Held by another process; try again shortly.
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose() => _held.Dispose();
}
