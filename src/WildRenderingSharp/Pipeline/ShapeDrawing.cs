using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>The one draw call every shape variant (G-buffer, z-only, forward) needs - bind its program, its own material UBO at binding 8, its sampler set, and issue the indexed draw.</summary>
public static class ShapeDrawing
{
    /// <param name="overrides">
    /// Sampler key -> a texture to bind instead of the one <paramref name="samplers"/> resolved for
    /// that key, or null for none. This is how a texture pattern anim takes effect: it re-points a
    /// sampler at a different texture without touching the program, the VAO, or the material UBO.
    /// </param>
    public static unsafe void Draw(GL gl, uint program, uint vao, uint materialUboBuffer,
        IReadOnlyList<ShapeSampler> samplers, int indexCount,
        IReadOnlyDictionary<string, LoadedTexture>? overrides = null)
    {
        if (vao == 0 || program == 0)
            return;

        gl.UseProgram(program);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, 8, materialUboBuffer);
        foreach (var (unit, key, texture) in samplers)
        {
            var bound = texture;
            if (overrides is not null && overrides.TryGetValue(key, out var replacement))
                bound = replacement;
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            gl.BindTexture(TextureTarget.Texture2D, bound.Handle);
        }
        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindVertexArray(vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, null);
    }
}
