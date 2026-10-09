using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>Issues the indexed draw of a shape variant, alone or for every instance of a batch, skipping the binds a pass has declared redundant.</summary>
internal sealed unsafe class ShapeDrawer : IDisposable
{
    public const uint BakeTableBinding = 0;

    readonly GL _gl;
    readonly DrawBindingCache _bound = new();
    readonly IndirectStream _stream;
    readonly InstancedRunWriter _runs;
    readonly Dictionary<uint, InstanceUniforms> _uniforms = [];
    readonly List<DrawCommand> _commands = [];
    InstanceUniforms _current;

    public ShapeDrawer(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;
        Programs = programs;
        _stream = new IndirectStream(gl);
        _runs = new InstancedRunWriter(gl, _stream, _bound);
    }

    public ShaderProgramCache Programs { get; }

    // Between these, a bind that matches the last one is skipped, so nothing else may bind these slots in the meantime.
    public void BeginStateCache() => _bound.Begin();

    public void EndStateCache() => _bound.End();

    public (long Triangles, long Instances) TakeCounts() => _runs.TakeCounts();

    public void Draw(uint program, uint vao, MaterialBlock material, IReadOnlyList<ShapeSampler> samplers, int indexCount,
        IReadOnlyDictionary<string, LoadedTexture>? overrides = null)
    {
        if (vao == 0 || program == 0)
            return;

        _bound.Invalidate();
        _gl.UseProgram(program);
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, Programs.Bindings.Material, material.Handle);
        foreach (var (unit, key, texture) in samplers)
        {
            var bound = Overridden(overrides, key, texture);
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _gl.BindTexture(bound.Target, bound.Handle);
        }
        _gl.ActiveTexture(TextureUnit.Texture0);

        _gl.BindVertexArray(vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, null);
    }

    public void DrawInstanced(uint program, uint vao, LoadedShape shape, IReadOnlyList<ShapeSampler> samplers, InstanceBatch batch,
        IReadOnlyList<(int First, int Count, int Lod)> runs)
    {
        if (vao == 0 || program == 0)
            return;

        UseInstancedProgram(program);
        BindBatchUniforms(batch);
        if (_bound.Needs(DrawBindingCache.Slot.Material, shape.MaterialBlock.Handle))
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, Programs.Bindings.Material, shape.MaterialBlock.Handle);

        var bake = BindSamplers(shape, samplers, batch);
        // Bound for every shape of a baked batch: instances still send their shader to this table even where the shape keeps its own bake0.
        if (batch.BakeAtlasOfInstance is not null && _bound.Needs(DrawBindingCache.Slot.BakeTable, batch.BakeTable))
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, BakeTableBinding, batch.BakeTable);
        if (_bound.Needs(DrawBindingCache.Slot.Vao, vao))
            _gl.BindVertexArray(vao);

        _runs.Draw(shape, batch, _current.First, bake, runs);
        if (!_bound.Enabled)
            RestoreOwnBake(bake);
    }

    // Draws the runs of a shape that needs no program of its own, as the shadow and pass-ID draws do; false when the driver has no base instance.
    public bool MultiDrawRuns(LoadedShape shape, IReadOnlyList<(int First, int Count, int Lod)> runs)
    {
        if (!InstancingContract.BaseInstance)
            return false;

        _commands.Clear();
        foreach (var (first, count, lod) in runs)
        {
            var (firstIndex, indexCount) = shape.Lod(lod);
            if (count > 0 && indexCount > 0)
                _commands.Add(new DrawCommand((uint)indexCount, (uint)count, (uint)firstIndex, 0, (uint)first));
        }
        _stream.Draw(_commands);
        return true;
    }

    public void Dispose() => _stream.Dispose();

    static LoadedTexture Overridden(IReadOnlyDictionary<string, LoadedTexture>? overrides, string key, LoadedTexture texture) =>
        overrides is not null && overrides.TryGetValue(key, out var replacement) ? replacement : texture;

    void UseInstancedProgram(uint program)
    {
        if (!_bound.Needs(DrawBindingCache.Slot.Program, program))
            return;
        _gl.UseProgram(program);
        if (!_uniforms.TryGetValue(program, out _current))
            _uniforms[program] = _current = InstanceUniforms.Find(_gl, program);
        _bound.ForgetBatchUniforms();
        // The first-instance uniform is zero for a multi-draw, where gl_BaseInstance carries it, and a program keeps its uniforms.
        if (InstancingContract.BaseInstance)
            _gl.Uniform1(_current.First, 0);
    }

    void BindBatchUniforms(InstanceBatch batch)
    {
        if (!_bound.NeedsBatchUniforms(batch))
            return;
        _gl.Uniform1(_current.Stride, batch.Stride);
        _gl.Uniform1(_current.PaletteVec4s, batch.PaletteVec4s);
        _gl.Uniform1(_current.PaletteRepeat, batch.PaletteRepeats ? 1 : 0);
    }

    // Binds the shape's samplers, holding back the bake0 one when each instance brings its own atlas; that one is returned for the run writer.
    BakeSlot? BindSamplers(LoadedShape shape, IReadOnlyList<ShapeSampler> samplers, InstanceBatch batch)
    {
        int[]? atlasOf = shape.HasBakeRegion ? batch.BakeAtlasOfInstance : null;
        BakeSlot? bake = null;
        foreach (var (unit, key, texture) in samplers)
        {
            if (atlasOf is not null && key == "bake0")
            {
                bake = new BakeSlot(unit, texture, atlasOf);
                continue;
            }
            var bound = Overridden(shape.SamplerOverrides, key, texture);
            _bound.BindTexture(_gl, unit, bound.Target, bound.Handle);
        }
        return bake;
    }

    // Outside the state cache, the material's own bake0 goes back where every other draw expects it.
    void RestoreOwnBake(BakeSlot? bake)
    {
        if (bake is { } slot)
            _bound.BindTexture(_gl, slot.Unit, slot.Own.Target, slot.Own.Handle);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }
}
