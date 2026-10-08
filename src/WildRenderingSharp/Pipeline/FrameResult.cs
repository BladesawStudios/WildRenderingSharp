namespace WildRenderingSharp.Pipeline;

/// <summary>The targets a viewer can show directly: the graded result and the intermediates behind it.</summary>
public readonly record struct FrameResult(GpuTexture Ldr, GpuTexture Final, GpuTexture Albedo, GpuTexture Normal, GpuTexture PreShadow, GpuTexture PreMisc, GpuTexture PassId);
