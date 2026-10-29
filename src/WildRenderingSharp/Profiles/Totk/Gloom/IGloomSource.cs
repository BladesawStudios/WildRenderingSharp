namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>A host's gloom for the frame about to be drawn - see <see cref="GloomPass"/>.</summary>
public interface IGloomSource
{
    // Null until the host has built its textures; no gloom is drawn until it has.
    GloomTextures? Textures { get; }

    GloomLook Look { get; }

    IReadOnlyList<GloomVolume> Volumes { get; }

    // The density of the Depths' altitude gloom over the whole view, or null where there is none.
    float? AltitudeDensity { get; }
}
