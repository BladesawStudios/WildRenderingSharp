using System.Diagnostics;
using System.Text;

namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>Starts the preparer executable and streams each line it writes, to either output, to a callback.</summary>
sealed class PreparerProcess(string executablePath)
{
    public bool IsAvailable => File.Exists(executablePath);

    // Returns the exit code once the process has ended and its output is drained; cancelling kills the process tree.
    public async Task<int> RunAsync(IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(arguments), EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) onLine(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) onLine(line); };
        process.Start();
        BelowNormal(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        // The parameterless wait drains the redirected streams, so the last line cannot be lost to a race with the exit.
        process.WaitForExit();
        return process.ExitCode;
    }

    // Preparing a map runs a dozen of these at once; at the host's own priority they take the cores its render thread needs.
    static void BelowNormal(Process process)
    {
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { /* already gone, or not ours to change */ }
    }

    ProcessStartInfo StartInfo(IReadOnlyList<string> arguments)
    {
        if (!IsAvailable)
            throw new FileNotFoundException(
                $"The model preparer was not found at '{executablePath}'. Hosts get it by importing build/WildRenderingSharp.targets.",
                executablePath);

        var info = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        };
        // The native apphost may be missing on some platforms, in which case the dll is run through the dotnet host.
        if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(executablePath);
        }
        else
        {
            info.FileName = executablePath;
        }
        foreach (string a in arguments)
            info.ArgumentList.Add(a);
        return info;
    }
}
