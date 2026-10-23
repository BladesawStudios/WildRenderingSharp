using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Names the animation archives a UMii piece's colours come from.</summary>
/// <remarks>
/// A UMii piece is prepared by its model's name, since an NPC names no model of its own. Its colour palettes are material animations, and
/// besides the archive beside the piece there is one shared by each race and kind of piece (<c>UMii_Hylia_Body</c>, <c>UMii_Hylia_Hair</c>),
/// which holds the skin's palette for every piece that shows skin.
/// </remarks>
static class TotkUMiiAnimations
{
    const string Prefix = "UMii_";

    static readonly string[] SharedKinds = ["Body", "Hair", "Glass", "Mustache", "Face", "Face_Edit"];

    // The piece's own archive and the shared ones of its race that exist, or null for a model that is not a UMii piece or has none.
    public static List<string>? ArchivesOf(IRomAccess rom, string modelName)
    {
        string project = modelName.Split('.')[0];
        string[] parts = project.Split('_');
        if (!project.StartsWith(Prefix, StringComparison.Ordinal) || parts.Length < 3)
            return null;

        var archives = new List<string>();
        foreach (string name in SharedKinds.Select(kind => $"{Prefix}{parts[1]}_{kind}").Prepend(project).Distinct())
            if (rom.Exists($"Model/{name}.anim.bfres.zs"))
                archives.Add(name);
        return archives.Count > 0 ? archives : null;
    }
}
