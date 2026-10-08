namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// Each <c>SystemModel.DeferredMain</c> pass's priority, the material ID it lights. The game draws a pass at the depth its priority names against a
/// depth buffer filled from the G-buffer's material IDs, so a pass lights exactly the pixels whose ID equals its priority. The priority is
/// <c>Mat</c> slot 25, <c>.x</c>, in the pass's own material block.
/// </summary>
public static class DeferredPassPriorities
{
    const int PriorityOffset = 25 * 16;

    /// <summary>The pass's priority, or null when its material file is missing or short.</summary>
    public static int? Read(string deferredMaterialsDirectory, string pass)
    {
        string path = Path.Combine(deferredMaterialsDirectory, $"{pass}.gsys_material.bin");
        if (!File.Exists(path))
            return null;
        byte[] bytes = File.ReadAllBytes(path);
        return bytes.Length >= PriorityOffset + 4 ? (int)MathF.Round(BitConverter.ToSingle(bytes, PriorityOffset)) : null;
    }

    /// <summary>
    /// The lighting passes by the non-zero ID they light. Priority 0 is what the G-buffer holds where nothing wrote an ID, which the field pass
    /// claims, and the <c>preshading_*</c> passes belong to the phase before.
    /// </summary>
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
