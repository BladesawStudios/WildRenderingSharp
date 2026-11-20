using System.Diagnostics;
using System.Text;

namespace KingRenderingSharp.Hosting.Preparers;

/// <summary>Starts the preparer executable and streams each line it writes, to either output, to a callback.</summary>
/// <remarks>
/// With <paramref name="keepAlive"/> the preparer is started once, with its <c>serve</c> command, and each run is a line sent to it; the process, the code it has compiled and what it has read stay for
/// the next run. A run that is cancelled ends the process, and the next one starts it again.
/// </remarks>
sealed class PreparerProcess(string executablePath, bool keepAlive = false)
{
    const string EndPrefix = "KRS_END ";

    readonly SemaphoreSlim _gate = new(1, 1);
    Process? _server;
    volatile Action<string>? _sink;

    public bool IsAvailable => File.Exists(executablePath);

    // Returns the exit code once the process has ended and its output is drained; cancelling kills the process tree.
    public async Task<int> RunAsync(IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken)
    {
        if (!keepAlive) return await RunOnceAsync(arguments, onLine, cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RunServedAsync(arguments, onLine, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    async Task<int> RunServedAsync(IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken)
    {
        Process process = Server();
        _sink = onLine;
        using CancellationTokenRegistration registration = cancellationToken.Register(() => Stop(process));
        try
        {
            await process.StandardInput.WriteLineAsync(string.Join('	', arguments)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            while (true)
            {
                string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    Stop(process);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IOException("The model preparer ended while it was working.");
                }

                if (line.StartsWith(EndPrefix, StringComparison.Ordinal)) return int.Parse(line[EndPrefix.Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                onLine(line);
            }
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            _sink = null;
        }
    }

    Process Server()
    {
        if (_server is { HasExited: false } running) return running;
        ProcessStartInfo info = StartInfo(["serve"]);
        info.RedirectStandardInput = true;
        Process process = new() { StartInfo = info };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) _sink?.Invoke(line); };
        process.Start();
        process.BeginErrorReadLine();
        return _server = process;
    }

    void Stop(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        if (ReferenceEquals(_server, process)) _server = null;
    }

    async Task<int> RunOnceAsync(IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken)
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
                $"The model preparer was not found at '{executablePath}'. Hosts get it by importing build/KingRenderingSharp.targets.",
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
