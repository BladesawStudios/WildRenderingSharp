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
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            writeTo(temp);
        }
        catch (DirectoryNotFoundException)
        {
            // The folder is made when a write finds it missing, not before every write: the folder nearly always exists, and asking the disk to make it is a call of its own.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            writeTo(temp);
        }
        File.Move(temp, path, overwrite: true);
    }
}
