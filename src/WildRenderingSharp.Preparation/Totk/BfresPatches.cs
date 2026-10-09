using System.Reflection;
using BfresLibrary;
using BfresLibrary.Core;
using BfresLibrary.Switch;
using BfresLibrary.Switch.Core;
using HarmonyLib;
using Syroot.BinaryData;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Runtime patches that let the vendored BfresLibrary parse TotK's V10 materials.</summary>
public static class BfresPatches
{
    static readonly object Gate = new();
    static bool _applied;

    static readonly FieldInfo BitFlagsField =
        typeof(MaterialParserV10.ShaderInfo).GetField("_optionBitFlags", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException("MaterialParserV10.ShaderInfo._optionBitFlags not found; BfresLibrary changed.");

    static readonly MethodInfo ContainsKey =
        typeof(ResDict).GetMethod("ContainsKey", [typeof(string)]) ?? throw new MissingMethodException("ResDict.ContainsKey not found.");

    public static void EnsureApplied()
    {
        lock (Gate)
        {
            if (_applied)
                return;
            var harmony = new Harmony("WildRenderingSharp.BfresPatches");
            Patch(harmony, typeof(MaterialParserV10.ShaderInfo), "SetupOptionBooleans", [typeof(int)], nameof(SetupOptionBooleansPrefix));
            Patch(harmony, typeof(ResDict), "Add", [typeof(string), typeof(IResData)], nameof(ResDictAddPrefix));
            Patch(harmony, typeof(ResFileSwitchLoader), "LoadString", [typeof(System.Text.Encoding)], nameof(LoadStringPrefix));
            _applied = true;
        }
    }

    static void Patch(Harmony harmony, Type type, string method, Type[] arguments, string prefix)
    {
        var target = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance, null, arguments, null)
            ?? throw new MissingMethodException($"{type.Name}.{method} not found; BfresLibrary changed.");
        var patch = typeof(BfresPatches).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)!;
        harmony.Patch(target, prefix: new HarmonyMethod(patch));
    }

    // A material with no boolean shader options stores a zero offset for its bit flags, which loads as null and then throws.
    static void SetupOptionBooleansPrefix(object __instance, int count)
    {
        if (BitFlagsField.GetValue(__instance) is null)
            BitFlagsField.SetValue(__instance, new long[1 + count / 64]);
    }

    // Names read through an unreadable offset all come back empty, and the second one collides on the key.
    static bool ResDictAddPrefix(object __instance, string key)
    {
        if (!(bool)ContainsKey.Invoke(__instance, [key])!)
            return true;
        Console.WriteLine($"[BfresPatches] skipped duplicate ResDict key \"{key}\"");
        return false;
    }

    // V10 names are 64-bit keys into the shared string table rather than offsets into the file, which the original reads as an out-of-range offset.
    static bool LoadStringPrefix(object __instance, ref string? __result)
    {
        var reader = (BinaryDataReader)__instance;
        long start = reader.Position;
        long offset = reader.ReadInt64();
        if (offset == 0 || (offset > 0 && offset <= reader.BaseStream.Length))
        {
            reader.Position = start;
            return true;
        }

        __result = ExternalStringTable.Lookup(unchecked((ulong)offset)) ?? "";
        return false;
    }
}
