using System.Text.Json;
using System.Text.Json.Nodes;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Content.Packs;

namespace KingRenderingSharp.Tests;

/// <summary>Writes a pack model source as the folder of JSON and binary files an older preparation wrote, so what the pack gives can be compared with what that preparation wrote. Tests only.</summary>
static class ModelDump
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static void Write(PackModelSource source, PackPrograms programs, string modelDirectory, string shaderDirectory)
    {
        Directory.CreateDirectory(modelDirectory);
        Directory.CreateDirectory(Path.Combine(modelDirectory, "matubo"));
        Directory.CreateDirectory(shaderDirectory);
        string name = source.Name;

        JsonObject manifest = (JsonObject)JsonSerializer.SerializeToNode(source.Manifest, Options)!;
        JsonArray shapes = (JsonArray)manifest["shapes"]!;
        for (int i = 0; i < source.Manifest.Shapes.Count; i++)
        {
            ShapeManifestEntry shape = source.Manifest.Shapes[i];
            JsonObject json = (JsonObject)shapes[i]!;
            json["shading_model"] = shape.ShadingModel;
            json["o_material_behave"] = shape.MaterialBehave;
            json["deferred_pass"] = shape.DeferredPass;
            foreach (string program in new[] { shape.GBufferShader, shape.ZOnlyShader, shape.MaterialShader })
                WriteProgram(programs, program, shaderDirectory);
            if (source.ParamLayout(shape.MaterialFile) is { } layout)
                File.WriteAllText(Path.Combine(modelDirectory, shape.MaterialFile[..^".gsys_material.bin".Length] + ".params.json"), JsonSerializer.Serialize(layout, Options));
            File.WriteAllText(Path.Combine(modelDirectory, $"{shape.Name.Replace(':', '_').Replace('/', '_')}_options.txt"), source.Options(shape.Name) ?? "");
        }

        File.WriteAllText(Path.Combine(modelDirectory, $"{name}.manifest.json"), manifest.ToJsonString(Options));
        if (source.Skeleton is { } skeleton) File.WriteAllText(Path.Combine(modelDirectory, $"{name}.skeleton.json"), JsonSerializer.Serialize(skeleton, Options));
        foreach (string anim in source.SkeletalAnims) File.WriteAllText(Path.Combine(modelDirectory, $"{name}.{anim}.anim.json"), JsonSerializer.Serialize(source.SkeletalAnim(anim), Options));
        foreach (string anim in source.MaterialAnims) File.WriteAllText(Path.Combine(modelDirectory, $"{name}.{anim}.matanim.json"), JsonSerializer.Serialize(source.MaterialAnim(anim), Options));
        foreach (string anim in source.TexturePatternAnims) File.WriteAllText(Path.Combine(modelDirectory, $"{name}.{anim}.texpat.json"), JsonSerializer.Serialize(source.TexturePatternAnim(anim), Options));
        foreach (string anim in source.VisibilityAnims) File.WriteAllText(Path.Combine(modelDirectory, $"{name}.{anim}.visanim.json"), JsonSerializer.Serialize(source.VisibilityAnim(anim), Options));
        foreach (string file in source.FileNames.ToList())
        {
            string path = Path.Combine(modelDirectory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, source.Bytes(file));
        }
    }

    static void WriteProgram(PackPrograms programs, string program, string shaderDirectory)
    {
        foreach (string extension in new[] { "vert", "frag" })
            if (programs.Exists($"{program}.{extension}"))
                File.WriteAllBytes(Path.Combine(shaderDirectory, $"{program}.{extension}"), programs.ReadBytes($"{program}.{extension}"));
    }
}
