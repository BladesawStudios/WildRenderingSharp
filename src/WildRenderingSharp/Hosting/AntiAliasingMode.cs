
namespace WildRenderingSharp.Hosting;

/// <summary>Anti-aliasing for a <see cref="SceneView"/>.</summary>
public enum AntiAliasingMode
{
    Off,
    Fxaa,
    /// <summary>Renders at twice the output size in each dimension and box-filters down in linear light.</summary>
    Supersample2x,
    Supersample2xFxaa,
}
