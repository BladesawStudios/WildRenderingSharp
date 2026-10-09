using WildRenderingSharp.Animation.Clips;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Animation.Posing;

/// <summary>
/// Applies a <see cref="TexturePatternAnimManifest"/> to a loaded model for one frame, by writing each affected shape's <see
/// cref="LoadedShape.SamplerOverrides"/>.
/// </summary>
public static class TexturePatternPose
{
    /// <summary>One anim and the frame to sample it at.</summary>
    public readonly record struct Playing(TexturePatternAnimManifest Anim, float Frame);

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

    public static void Clear(LoadedModel model)
    {
        foreach (var shape in model.Shapes)
            shape.SamplerOverrides = null;
    }
}
