namespace WildRenderingSharp.Profiles.Totk.Deferred.PassIds;

/// <summary>Each <c>SystemModel.DeferredMain</c> pass's priority (<c>Mat</c> slot 25, <c>.x</c>), the material ID it lights.</summary>
internal static class DeferredPassPriorities
{
    const int PriorityOffset = 25 * 16;

    public static int? Read(string deferredMaterialsDirectory, string pass)
    {
        string path = Path.Combine(deferredMaterialsDirectory, $"{pass}.gsys_material.bin");
        if (!File.Exists(path))
            return null;
        byte[] bytes = File.ReadAllBytes(path);
        return bytes.Length >= PriorityOffset + 4 ? (int)MathF.Round(BitConverter.ToSingle(bytes, PriorityOffset)) : null;
    }

    // The lighting passes by the non-zero ID they light. Priority 0 is what the field pass claims, and preshading_* belong to the phase before.
    public static IReadOnlyDictionary<int, string> ByPriority(string deferredMaterialsDirectory)
    {
        var passes = new SortedDictionary<int, string>();
        if (!Directory.Exists(deferredMaterialsDirectory))
            return passes;
        foreach (string file in Directory.EnumerateFiles(deferredMaterialsDirectory, "*.gsys_material.bin").Order(StringComparer.Ordinal))
        {
            string pass = Path.GetFileName(file)[..^".gsys_material.bin".Length];
            if (pass.StartsWith("preshading_", StringComparison.Ordinal) || pass == "debug_simple")
                continue;
            if (Read(deferredMaterialsDirectory, pass) is int priority and > 0)
                passes.TryAdd(priority, pass);
        }
        return passes;
    }
}
