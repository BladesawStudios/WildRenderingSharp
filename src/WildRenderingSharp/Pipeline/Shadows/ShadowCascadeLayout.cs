namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>How a cascade kept between frames is laid out against the view depths it serves.</summary>
/// <remarks>
/// A cascade that is a region around the view serves the depths up to a share of its reach, which keeps the lateral distance of a pixel it serves inside the region it is fitted to.
/// A host that knows where it wants a cascade's slice to end gives it the reach that serves up to there.
/// </remarks>
public static class ShadowCascadeLayout
{
    public const float ReachShare = 0.6f;

    // The reach of a region cascade whose slice of the view ends at a view depth.
    public static float ReachFor(float sliceEnd) => sliceEnd / ReachShare;
}
