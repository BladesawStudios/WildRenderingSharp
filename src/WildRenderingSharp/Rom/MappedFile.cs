using System.IO.MemoryMappedFiles;

namespace WildRenderingSharp.Rom;

/// <summary>A file read through a memory map.</summary>
sealed unsafe class MappedFile : IDisposable
{
    readonly MemoryMappedFile? _file;
    readonly MemoryMappedViewAccessor? _view;
    readonly byte* _pointer;
    readonly int _length;
    bool _disposed;

    public MappedFile(string path)
    {
        long length = new FileInfo(path).Length;
        if (length > int.MaxValue)
            throw new IOException($"'{path}' is larger than 2 GB.");
        _length = (int)length;
        if (_length == 0)
            return;

        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
    }

    public ReadOnlySpan<byte> Span => _length == 0 ? default : new ReadOnlySpan<byte>(_pointer, _length);

    // Releases the map if its owner never disposed it.
    ~MappedFile() => Dispose();

    public void Dispose()
    {
        if (_view is null || _disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file!.Dispose();
    }
}
