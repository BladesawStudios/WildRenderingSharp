using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Applies a <see cref="TexturePatternAnimManifest"/> to a loaded model for one frame, by writing
/// each affected shape's <see cref="LoadedShape.SamplerOverrides"/>. Nothing else in the pipeline
/// needs to know a pattern anim exists: <see cref="WildRenderingSharp.Pipeline.ShapeDrawing"/> substitutes
/// by sampler key while binding, so the G-buffer, z-only and forward variants all pick it up even
/// though the same sampler sits on a different unit in each one's compiled program.
///
/// This is the runtime half of what <c>nn::g3d2::MaterialAnimObj::ApplyTo</c> (Ghidra 0x7100080894)
/// does - it writes the texture the pattern curve selected into the material object's sampler slot.
/// The selection itself is <see cref="TexturePatternSamplerEntry.Evaluate"/>, a step curve; see
/// <c>ShaderLibrary.CompileTool.ExportTexturePatternAnim</c> for the full derivation.
/// </summary>
public static class TexturePatternPose
{
    /// <summary>One anim and the frame to sample it at.</summary>
    public readonly record struct Playing(TexturePatternAnimManifest Anim, float Frame);

    /// <summary>
    /// Points every sampler these anims drive at its current texture, and clears the override on
    /// every other shape so a previously-applied anim cannot linger.
    ///
    /// Several anims apply together because they genuinely do: Enemy_Dragon_Darkness's four
    /// <c>Weakness_0N_Death_ftp</c> anims each drive a different material, so showing more than one
    /// broken weak point means running more than one of them. Where two do overlap on the same
    /// sampler, the later one wins - the same ordering the engine's sequential ApplyTo gives.
    /// </summary>
    /// <param name="textures">
    /// The model's texture cache - pattern textures are loaded through it on demand and cached
    /// like any other, so switching anims or scrubbing costs one upload per distinct texture and
    /// nothing thereafter.
    /// </param>
    public static void Apply(LoadedModel model, IReadOnlyList<Playing> playing, TextureCache textures)
    {
        var byMaterial = new Dictionary<string, Dictionary<string, LoadedTexture>>(StringComparer.Ordinal);

        foreach (var (anim, frame) in playing)
        {
            foreach (var mat in anim.Materials)
            {
                foreach (var sampler in mat.Samplers)
                {
                    int index = sampler.Evaluate(frame);
                    var binding = anim.BindingFor(index, sampler.Sampler);
                    if (binding is null)
                        continue;
                    var tex = textures.Load(binding);
                    if (tex is null)
                        continue;
                    if (!byMaterial.TryGetValue(mat.Material, out var overrides))
                        byMaterial[mat.Material] = overrides = new Dictionary<string, LoadedTexture>(StringComparer.Ordinal);
                    overrides[sampler.Sampler] = tex;
                }
            }
        }

        // One material can back several shapes, and the swap applies to all of them - the anim
        // names a MATERIAL, not a shape.
        foreach (var shape in model.Shapes)
            shape.SamplerOverrides = byMaterial.GetValueOrDefault(shape.Material);
    }

    /// <summary>Drops every override, putting the model back on exactly the textures its materials declare.</summary>
    public static void Clear(LoadedModel model)
    {
        foreach (var shape in model.Shapes)
            shape.SamplerOverrides = null;
    }
}
