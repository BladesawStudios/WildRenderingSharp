using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Gpu;

/// <summary>State of type <typeparamref name="T"/> that belongs to a GL context, created on first use and dropped with the context.</summary>
internal static class ContextState<T> where T : class, new()
{
    static readonly ConditionalWeakTable<GL, T> States = new();

    public static T For(GL gl) => States.GetOrCreateValue(gl);
}
