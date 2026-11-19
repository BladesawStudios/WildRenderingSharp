using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Games.Totk.Deferred.PassIds;

/// <summary>Each <c>SystemModel.DeferredMain</c> pass's priority (<c>Mat</c> slot 25, <c>.x</c>), the material ID it lights.</summary>
internal static class DeferredPassPriorities
{
    const int PriorityOffset = 25 * 16;

    public static int? Read(IContentFiles deferredMaterials, string pass)
    {
        string name = $"{pass}.gsys_material.bin";
        if (!deferredMaterials.Exists(name))
            return null;
        byte[] bytes = deferredMaterials.ReadBytes(name);
        return bytes.Length >= PriorityOffset + 4 ? (int)MathF.Round(BitConverter.ToSingle(bytes, PriorityOffset)) : null;
    }

    // The lighting passes by the non-zero ID they light. Priority 0 is what the field pass claims, and preshading_* belong to the phase before.
    public static IReadOnlyDictionary<int, string> ByPriority(IContentFiles deferredMaterials)
    {
        var passes = new SortedDictionary<int, string>();
        foreach (string file in deferredMaterials.Enumerate("*.gsys_material.bin").Order(StringComparer.Ordinal))
        {
            string pass = file[..^".gsys_material.bin".Length];
            if (pass.StartsWith("preshading_", StringComparison.Ordinal) || pass == "debug_simple")
                continue;
            if (Read(deferredMaterials, pass) is int priority and > 0)
                passes.TryAdd(priority, pass);
        }
        return passes;
    }
}
