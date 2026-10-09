namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>Keys of the uniform blocks only TotK's frame keeps.</summary>
internal static class TotkUniformKeys
{
    /// <summary>The scene's camera block with the frame declared as a single tile, for <c>field_hybrid</c>'s vertex stage.</summary>
    public const string FieldCamera = "ctx_field";
}
