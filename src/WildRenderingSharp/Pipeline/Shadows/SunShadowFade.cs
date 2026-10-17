namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>The view depths between which a sun shadow fades to lit, as the game's shadow pre-pass authors them (<c>master_field</c>'s common shadow settings).</summary>
internal static class SunShadowFade
{
    public const float Start = 400f, End = 500f;
}
