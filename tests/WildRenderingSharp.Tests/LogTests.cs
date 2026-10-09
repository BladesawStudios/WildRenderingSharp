using WildRenderingSharp.Logging;

namespace WildRenderingSharp.Tests;

[Collection("Log")]
public class LogTests
{
    [Fact]
    public void ASinkReceivesEachMessageWithItsLevel()
    {
        var seen = new List<(LogLevel, string)>();
        var before = Log.Sink;
        try
        {
            Log.Sink = (level, message) => { lock (seen) seen.Add((level, message)); };
            Log.Info("loaded");
            Log.Warning("skipped");
            Log.Error("broke");
        }
        finally
        {
            Log.Sink = before;
        }

        // Other tests run beside this one and may log through the same sink.
        var ours = seen.Where(m => m.Item2 is "loaded" or "skipped" or "broke").ToList();
        Assert.Equal([(LogLevel.Info, "loaded"), (LogLevel.Warning, "skipped"), (LogLevel.Error, "broke")], ours);
    }

    [Fact]
    public void ANullSinkSilencesTheLog()
    {
        var before = Log.Sink;
        try
        {
            Log.Sink = null;
            Log.Error("nobody hears this");
        }
        finally
        {
            Log.Sink = before;
        }
    }
}
