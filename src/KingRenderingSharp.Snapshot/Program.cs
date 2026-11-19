using KingRenderingSharp.Snapshot;

// The server reads commands from standard input (see Protocol) and writes answers to standard output. Anything else the renderer prints goes to standard error, so it cannot
// land in the middle of a frame.
Stream answers = Console.OpenStandardOutput();
Console.SetOut(Console.Error);
using Server server = new(Console.OpenStandardInput(), answers);
return server.Run();
