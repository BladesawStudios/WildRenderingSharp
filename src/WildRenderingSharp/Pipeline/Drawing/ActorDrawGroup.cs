using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>
/// One placed actor's shapes plus the per-actor GPU skinning resources every shape-drawing pass needs bound before drawing them -
/// the real compiled game shaders read bone transforms from shared UBO binding points (<c>_Mtx</c>/binding 2, <c>ShpMtx</c>/binding
/// 4) with no per-draw instance addressing at all, so multi-actor rendering works by REBINDING a different actor's own buffer
/// immediately before that actor's own draw calls - exactly what a real engine does between per-object draw calls - rather than
/// trying to combine every actor into one buffer.
/// </summary>
public readonly record struct ActorDrawGroup(IReadOnlyList<Ubo> Uniforms, Vector4[] ModelMatrixRows, IReadOnlyList<LoadedShape> Shapes,
    InstanceBatch? Batch = null, bool ShadowRuns = false, int Cascade = -1, IReadOnlyList<UboSpec>? ZeroedBlocks = null)
{
    // A group that draws every instance of a batch, whose transforms come from the batch's buffer rather than the actor's blocks.
    public static ActorDrawGroup ForBatch(IGameProfile profile, InstanceBatch batch, IReadOnlyList<LoadedShape> shapes, bool shadowRuns = false, int cascade = -1) =>
        new([], GpuMatrix.Rows(Matrix4x4.Identity, 3), shapes, batch, shadowRuns, cascade, profile.InstancedActorBlocks);

    List<(int First, int Count, int Lod)>? Runs => Batch is null ? null
        : ShadowRuns ? (Cascade >= 0 ? Batch.Shadow.Cascade(Cascade) : Batch.Shadow.Visible) : Batch.Visible;

    public string Label => Batch is { } b ? Path.GetFileName(b.Model.DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : (Shapes.Count > 0 ? Shapes[0].Name : "actor");

    public void BindInstanceBuffer(GLResourceCache resources) =>
        resources.Gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, InstancingContract.InstanceBinding, Batch!.Buffer);

    public void BindUbos(GLResourceCache resources)
    {
        resources.Bind(Uniforms);
        foreach (var block in ZeroedBlocks ?? [])
            resources.BindZeroed(block);
        if (Batch is not null)
            BindInstanceBuffer(resources);
    }

    public void Draw(ShapeDrawer drawer, LoadedShape shape, ShapeProgram which)
    {
        uint vao = which switch { ShapeProgram.ZOnly => shape.ZOnlyVao, ShapeProgram.Forward => shape.ForwardVao, _ => shape.GBufferVao };
        var samplers = which switch { ShapeProgram.ZOnly => shape.ZOnlySamplers, ShapeProgram.Forward => shape.ForwardSamplers, _ => shape.GBufferSamplers };

        if (Batch is not { } batch)
        {
            uint program = which switch { ShapeProgram.ZOnly => shape.ZOnlyProgram, ShapeProgram.Forward => shape.ForwardProgram, _ => shape.GBufferProgram };
            drawer.Draw(program, vao, shape.MaterialBlock, samplers, shape.IndexCount, shape.SamplerOverrides);
            return;
        }

        var runs = Runs!;
        if (runs.Count == 0)
            return;
        EnsureInstancedPrograms(drawer.Programs, shape);
        uint instanced = which switch { ShapeProgram.ZOnly => shape.InstancedZOnlyProgram, ShapeProgram.Forward => shape.InstancedForwardProgram, _ => shape.InstancedGBufferProgram };
        drawer.DrawInstanced(instanced, vao, shape, samplers, batch, runs);
    }

    internal static void EnsureInstancedPrograms(ShaderProgramCache programs, LoadedShape shape)
    {
        if (shape.InstancedProgramsLinked)
            return;
        shape.InstancedProgramsLinked = true;
        if (!string.IsNullOrEmpty(shape.GBufferShaderName))
            shape.InstancedGBufferProgram = programs.LoadInstanced(shape.GBufferShaderName);
        if (!string.IsNullOrEmpty(shape.ZOnlyShaderName))
            shape.InstancedZOnlyProgram = programs.LoadInstanced(shape.ZOnlyShaderName);
        if (!string.IsNullOrEmpty(shape.ForwardShaderName))
            shape.InstancedForwardProgram = programs.LoadInstanced(shape.ForwardShaderName, isForwardProgram: true);
    }
}
