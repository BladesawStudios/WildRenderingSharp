namespace WildRenderingSharp.Preparation;

/// <summary>Thread-safe text writer wrapper that flushes each line.</summary>
sealed class ProtocolWriter(TextWriter output)
{
    readonly object _gate = new();

    public void Say(string line)
    {
        lock (_gate)
        {
            output.WriteLine(line);
            output.Flush();
        }
    }
}
