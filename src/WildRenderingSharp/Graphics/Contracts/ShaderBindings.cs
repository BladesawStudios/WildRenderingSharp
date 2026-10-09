using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Graphics.Contracts;

/// <summary>The binding points a game's shaders read the blocks the renderer shares across passes from.</summary>
public readonly record struct ShaderBindings(uint Camera, uint Environment, uint Material);
