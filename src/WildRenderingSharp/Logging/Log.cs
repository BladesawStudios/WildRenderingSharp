namespace WildRenderingSharp.Logging;

/// <summary>How serious a log message is.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Where the library's messages go: the console unless a host sets <see cref="Sink"/> to take them.</summary>
public static class Log
{
    static volatile Action<LogLevel, string> _sink = WriteToConsole;

    // Setting it to null silences the library.
    public static Action<LogLevel, string>? Sink
    {
        get => _sink;
        set => _sink = value ?? (static (_, _) => { });
    }

    public static void Info(string message) => _sink(LogLevel.Info, message);

    public static void Warning(string message) => _sink(LogLevel.Warning, message);

    public static void Error(string message) => _sink(LogLevel.Error, message);

    // Errors go to the error stream, the rest to standard output, where the out-of-process preparer's parent reads its progress.
    static void WriteToConsole(LogLevel level, string message)
    {
        if (level == LogLevel.Error)
            Console.Error.WriteLine(message);
        else
            Console.WriteLine(message);
    }
}
