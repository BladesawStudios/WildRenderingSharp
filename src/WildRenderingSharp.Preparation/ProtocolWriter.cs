namespace WildRenderingSharp.Preparation;

/// <summary>Writes the WRS_ progress lines a batch parent reads, one whole line at a time from any thread.</summary>
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
