using System.IO.MemoryMappedFiles;

namespace WildRenderingSharp.Rom;

/// <summary>A file read through a memory map, so a large archive costs address space rather than a copy.</summary>
sealed unsafe class MappedFile : IDisposable
{
    readonly MemoryMappedFile? _file;
    readonly MemoryMappedViewAccessor? _view;
    readonly byte* _pointer;
    readonly int _length;

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

    public void Dispose()
    {
        if (_view is null)
            return;
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file!.Dispose();
    }
}
