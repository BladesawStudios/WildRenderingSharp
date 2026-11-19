using KingRenderingSharp.Content.Animation.Clips;
using KingRenderingSharp.Content.Assets;

namespace KingRenderingSharp.Content.Animation.Posing;

/// <summary>Applies visibility animations to a loaded model for one frame: a shape is hidden when an animation hides its material, or the bone it is attached to.</summary>
internal static class VisibilityPose
{
    public readonly record struct Playing(VisibilityAnimManifest Anim, float Frame);

    public static void Apply(LoadedModel model, IReadOnlyList<Playing> playing)
    {
        foreach (LoadedShape shape in model.Shapes)
        {
            string? bone = model.Skeleton is { } skeleton && (uint)shape.BoneIndex < (uint)skeleton.Bones.Count ? skeleton.Bones[shape.BoneIndex].Name : null;
            bool hidden = false;
            foreach ((VisibilityAnimManifest anim, float frame) in playing)
            {
                string? key = anim.IsBone ? bone : shape.Material;
                if (key is null) continue;
                foreach (VisibilityAnimEntry entry in anim.Entries)
                {
                    if (string.Equals(entry.Name, key, StringComparison.Ordinal) && !entry.VisibleAt(frame)) hidden = true;
                }
            }

            shape.AnimationHidden = hidden;
        }
    }

    public static void Clear(LoadedModel model)
    {
        foreach (LoadedShape shape in model.Shapes) shape.AnimationHidden = false;
    }
}
