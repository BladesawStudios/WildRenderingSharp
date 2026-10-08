using System.Security.Cryptography;

namespace WildRenderingSharp.Tests;

/// <summary>
/// Pins a byte buffer to the SHA-256 recorded in <c>Snapshots/&lt;name&gt;.sha256</c>. Run with
/// <c>WRS_UPDATE_SNAPSHOTS=1</c> to (re)write the recorded hashes.
/// </summary>
static class Snapshot
{
    public static void Verify(string name, byte[] bytes)
    {
        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        string recorded = Path.Combine(SourceSnapshotDirectory(), name + ".sha256");

        if (Environment.GetEnvironmentVariable("WRS_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(recorded, actual + "\n");
            return;
        }

        Assert.True(File.Exists(recorded), $"No snapshot recorded for '{name}'. Run with WRS_UPDATE_SNAPSHOTS=1.");
        Assert.Equal(File.ReadAllText(recorded).Trim(), actual);
    }

    static string SourceSnapshotDirectory([System.Runtime.CompilerServices.CallerFilePath] string callerFile = "") =>
        Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
}
