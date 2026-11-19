using System.Reflection;

namespace KingRenderingSharp.Tests;

// The types a host can see are a decision, not an accident: adding one means recording it in Snapshots/public_types.txt.
public class PublicApiTests
{
    [Fact]
    public void ThePublicTypesAreTheRecordedOnes()
    {
        var actual = typeof(KingRenderer).Assembly.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("KingRenderingSharp.", StringComparison.Ordinal) && a.Name != "KingRenderingSharp.Preparation").Select(Assembly.Load).Prepend(typeof(KingRenderer).Assembly).SelectMany(a => a.GetExportedTypes())
            .Where(t => !t.IsNested)
            .Select(t => t.FullName!.Replace("KingRenderingSharp.", ""))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string recorded = Path.Combine(SnapshotDirectory(), "public_types.txt");

        if (Environment.GetEnvironmentVariable("KRS_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllLines(recorded, actual);
            return;
        }

        Assert.Equal(File.ReadAllLines(recorded), actual);
    }

    static string SnapshotDirectory([System.Runtime.CompilerServices.CallerFilePath] string callerFile = "") =>
        Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
}
