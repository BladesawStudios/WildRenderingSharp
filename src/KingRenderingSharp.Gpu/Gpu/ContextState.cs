using System.Runtime.CompilerServices;

namespace KingRenderingSharp.Gpu;

/// <summary>State of type <typeparamref name="T"/> that belongs to a GL context, created on first use and dropped with the context.</summary>
internal static class ContextState<T> where T : class, new()
{
    static readonly ConditionalWeakTable<GpuApi, T> States = new();

    public static T For(GpuApi gl) => States.GetOrCreateValue(gl);
}
