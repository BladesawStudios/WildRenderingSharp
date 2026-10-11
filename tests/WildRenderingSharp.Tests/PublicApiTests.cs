using System.Reflection;

namespace WildRenderingSharp.Tests;

// The types a host can see are a decision, not an accident: adding one means recording it in Snapshots/public_types.txt.
public class PublicApiTests
{
    [Fact]
    public void ThePublicTypesAreTheRecordedOnes()
    {
        var actual = typeof(WildRenderer).Assembly.GetExportedTypes()
            .Where(t => !t.IsNested)
            .Select(t => t.FullName!.Replace("WildRenderingSharp.", ""))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string recorded = Path.Combine(SnapshotDirectory(), "public_types.txt");

        if (Environment.GetEnvironmentVariable("WRS_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllLines(recorded, actual);
            return;
        }

        Assert.Equal(File.ReadAllLines(recorded), actual);
    }

    static string SnapshotDirectory([System.Runtime.CompilerServices.CallerFilePath] string callerFile = "") =>
        Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
}
