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
            gl.BindTexture(bound.Target, bound.Handle);
        }
        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindVertexArray(vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, null);
    }

    /// <summary>
    /// <see cref="Draw"/> for every visible run of a batch: the same program state, then one
    /// instanced call per run, each at that run's level of detail. The instance buffer is already
    /// bound (<see cref="ActorDrawGroup.BindUbos"/>).
    /// </summary>
    public static unsafe void DrawInstanced(GL gl, uint program, uint vao, LoadedShape shape,
        IReadOnlyList<ShapeSampler> samplers, InstanceBatch batch, IReadOnlyList<(int First, int Count, int Lod)>? runs = null)
    {
        if (vao == 0 || program == 0)
            return;

        gl.UseProgram(program);
        var locations = InstancedLocations(gl, program);
        gl.Uniform1(locations.Stride, batch.Stride);
        gl.Uniform1(locations.PaletteVec4s, batch.PaletteVec4s);
        gl.Uniform1(locations.PaletteRepeat, batch.PaletteRepeats ? 1 : 0);

        gl.BindBufferBase(BufferTargetARB.UniformBuffer, 8, shape.MaterialUboBuffer);
        var overrides = shape.SamplerOverrides;
        foreach (var (unit, key, texture) in samplers)
        {
            var bound = texture;
            if (overrides is not null && overrides.TryGetValue(key, out var replacement))
                bound = replacement;
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            gl.BindTexture(bound.Target, bound.Handle);
        }
        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindVertexArray(vao);
        foreach (var (first, count, lod) in runs ?? batch.Visible)
        {
            if (count <= 0)
                continue;
            var (firstIndex, indexCount) = shape.Lod(lod);
            if (indexCount <= 0)
                continue;
            gl.Uniform1(locations.First, first);
            gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt,
                (void*)((nint)firstIndex * sizeof(uint)), (uint)count);
        }
    }

    /// <summary>Drops every remembered location - the programs they were looked up in are being deleted.</summary>
    internal static void ForgetPrograms(IEnumerable<uint> programs)
    {
        foreach (uint program in programs)
            LocationCache.Remove(program);
    }

    readonly record struct Locations(int First, int Stride, int PaletteVec4s, int PaletteRepeat);

    static readonly Dictionary<uint, Locations> LocationCache = [];

    /// <summary>
    /// The instancing uniforms' locations, looked up once per program. Keyed by program name, which
    /// GL reuses after a delete - so <see cref="ShaderProgramCache"/> calls <see cref="ForgetPrograms"/>
    /// when it deletes the programs these belong to.
    /// </summary>
    static Locations InstancedLocations(GL gl, uint program)
    {
        if (!LocationCache.TryGetValue(program, out var l))
        {
            l = new Locations(
                gl.GetUniformLocation(program, InstancedShaderPatch.FirstInstanceUniform),
                gl.GetUniformLocation(program, InstancedShaderPatch.StrideUniform),
                gl.GetUniformLocation(program, InstancedShaderPatch.PaletteVec4sUniform),
                gl.GetUniformLocation(program, InstancedShaderPatch.PaletteRepeatUniform));
            LocationCache[program] = l;
        }
        return l;
    }
}
