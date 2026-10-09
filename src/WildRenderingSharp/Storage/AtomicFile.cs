namespace WildRenderingSharp.Storage;

/// <summary>Writes a file aside and moves it into place, so a reader never sees half of it.</summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        byte[] copy = bytes.ToArray();
        Write(path, temp => File.WriteAllBytes(temp, copy));
    }

    public static void WriteAllText(string path, string text) => Write(path, temp => File.WriteAllText(temp, text));

    public static void Write(string path, Action<string> writeTo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        writeTo(temp);
        File.Move(temp, path, overwrite: true);
    }
}
