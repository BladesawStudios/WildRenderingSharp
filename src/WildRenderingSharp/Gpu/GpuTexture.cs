

namespace WildRenderingSharp.Gpu;

/// <summary>A GL texture handle with its size.</summary>
public readonly record struct GpuTexture(uint Handle, int Width, int Height);
