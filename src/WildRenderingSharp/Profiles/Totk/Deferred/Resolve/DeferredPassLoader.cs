using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>Links the game's extracted resolve program and loads the <c>gsys_material</c> block for each deferred pass a model's shapes name.</summary>
static class DeferredPassLoader
{
    public const string FieldFallbackPass = "chara_nonmetal";

    public static List<ResolvedDeferredPass> Load(GL gl, ShaderProgramCache programs, string decompiledDir, string deferredMaterialsDir,
        IEnumerable<string> passNames)
    {
        var resolved = new List<ResolvedDeferredPass>();
        int passIndex = -1;
        foreach (string requested in passNames)
        {
            passIndex++;
            if (FindProgram(decompiledDir, requested) is not var (name, source))
                continue;

            // The resolve is drawn at the frame's size, so the depth it gathers at its own pixel is the full-resolution one.
            uint program = programs.Load(source, patchFragment: f => requested == MiasmaDebug.Pass
                ? MiasmaDebug.Patch(PixelCentreGather.Anchor(f, "cTex_NormalizedLinearDepth"))
                : PixelCentreGather.Anchor(f, "cTex_NormalizedLinearDepth"));
            string materialPath = Path.Combine(deferredMaterialsDir, $"{name}.gsys_material.bin");
            var material = MaterialBlock.FromFile(gl, materialPath);
            if (material.Authored.Length == 0)
                Log.Warning($"  [warn] no deferred gsys_material for pass '{name}' at '{materialPath}' - resolving with an all-zero Mat block");

            resolved.Add(new ResolvedDeferredPass(requested, program, material, passIndex,
                FieldLights: name.StartsWith("field_", StringComparison.Ordinal), Tiled: name is "field_hybrid" or "field_hybrid_all_shadow", Source: source));
        }
        return resolved;
    }

    // The pass and program a requested name resolves to. A pass with no program of its own (an o_material_behave nothing maps,
    // exported as an empty name) is lit as the common case rather than left unlit.
    static (string Name, string Source)? FindProgram(string decompiledDir, string name)
    {
        if (Find(decompiledDir, name) is { } own)
            return (name, own);
        if (name == FieldFallbackPass)
        {
            Log.Warning($"  [skip] no extracted deferred shader for pass '{name}'");
            return null;
        }

        Log.Warning($"  [approx] no extracted deferred shader for pass '{name}'; resolving through {FieldFallbackPass}");
        if (Find(decompiledDir, FieldFallbackPass) is { } fallback)
            return (FieldFallbackPass, fallback);
        Log.Warning($"  [skip] no extracted deferred shader for pass '{FieldFallbackPass}'");
        return null;
    }

    // The program a pass is extracted as, by its file name without the extension, or null when it was not extracted.
    public static string? Find(string decompiledDir, string name) =>
        name.Length == 0 ? null
            : Directory.EnumerateFiles(decompiledDir, $"deferred_{name}_prog*_extracted.frag")
                .OrderBy(f => f, StringComparer.Ordinal).Select(Path.GetFileNameWithoutExtension).FirstOrDefault();
}
