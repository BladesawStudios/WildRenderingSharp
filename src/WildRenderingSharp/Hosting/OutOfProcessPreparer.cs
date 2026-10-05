using System.Diagnostics;
using System.Text;

namespace WildRenderingSharp.Hosting;

/// <summary>
/// Runs <c>WildRenderingSharp.Preparation</c> as a child process - see <see cref="IModelPreparer"/>
/// for when a host needs this rather than preparing in-process.
/// </summary>
/// <remarks>
/// <para>
/// The executable is expected at <c>&lt;app&gt;/wrs-prepare/WildRenderingSharp.Preparation(.exe)</c>,
/// which is where <c>build/WildRenderingSharp.targets</c> puts it. Its command line is documented
/// in that project's <c>Program.cs</c>; this class is its only client in this repository.
/// </para>
/// <para>
/// Progress lines from the child are forwarded to the caller's log as they arrive, so a long first
/// preparation (decompiling dozens of shader programs) shows life rather than hanging silently.
/// </para>
/// </remarks>
public sealed class OutOfProcessPreparer : IModelPreparer
{
    /// <summary>The line the child prints last on success: <c>WRS_RESULT &lt;resolved model name&gt;</c>.</summary>
    public const string ResultPrefix = "WRS_RESULT ";

    public string ExecutablePath { get; }

    /// <param name="executablePath">The preparer, or null for the default <c>wrs-prepare</c> folder beside this process.</param>
    public OutOfProcessPreparer(string? executablePath = null)
    {
        ExecutablePath = executablePath ?? DefaultExecutablePath();
    }

    public static string DefaultExecutablePath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "wrs-prepare");
        string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "WildRenderingSharp.Preparation.exe" : "WildRenderingSharp.Preparation");
        // Framework-dependent builds always have the .dll; the native apphost may be missing on
        // some platforms, in which case the dll is run through the dotnet host instead.
        return File.Exists(exe) ? exe : Path.Combine(dir, "WildRenderingSharp.Preparation.dll");
    }

    public bool IsAvailable => File.Exists(ExecutablePath);

    public async Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        await RunAsync(["ensure-system", "--romfs", romfsRoot, "--cache", cache.Root], log, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "prepare",
            "--romfs", request.RomfsRoot,
            "--actor", request.ActorOrModelName,
            "--cache", request.Cache.Root,
        };
        foreach (string mod in request.ModRomfsLayers ?? [])
        {
            args.Add("--mod");
            args.Add(mod);
        }
        if (!request.ImportAnimations)
            args.Add("--no-anims");
        if (request.Force)
            args.Add("--force");

        string? result = await RunAsync(args, log, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"The preparer finished without naming the model it prepared for '{request.ActorOrModelName}'.");
    }

    async Task<string?> RunAsync(IReadOnlyList<string> arguments, Action<string>? log, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            throw new FileNotFoundException(
                $"The model preparer was not found at '{ExecutablePath}'. Hosts get it by importing build/WildRenderingSharp.targets.",
                ExecutablePath);

        var info = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
        };
        if (ExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(ExecutablePath);
        }
        else
        {
            info.FileName = ExecutablePath;
        }
        foreach (string a in arguments)
            info.ArgumentList.Add(a);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        string? result = null;
        var tail = new Queue<string>();
        void Keep(string line)
        {
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 40)
                    tail.Dequeue();
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not { } line)
                return;
            if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
            {
                result = line[ResultPrefix.Length..].Trim();
                return;
            }
            Keep(line);
            log?.Invoke(line);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { } line)
                return;
            Keep(line);
            log?.Invoke(line);
        };

        process.Start();
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
        // The parameterless wait drains the redirected streams, so the result line cannot be lost
        // to a race between process exit and the last OutputDataReceived.
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail)
                detail = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException($"The model preparer failed (exit code {process.ExitCode}):{Environment.NewLine}{detail}");
        }
        return result;
    }
}
