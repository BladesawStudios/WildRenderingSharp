namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>Keys of the uniform blocks only TotK's frame keeps.</summary>
internal static class TotkUniformKeys
{
    // The scene's camera block with the frame declared as a single tile, for field_hybrid's vertex stage.
    public const string FieldCamera = "ctx_field";
}
