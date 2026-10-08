using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline;

/// <summary>Which of a shape's three programs a pass draws it with.</summary>
public enum ShapeProgram
{
    GBuffer,
    ZOnly,
    Forward,
}

/// <summary>
/// One placed actor's shapes plus the per-actor GPU skinning resources every shape-drawing pass
/// needs bound before drawing them - the real compiled game shaders read bone transforms from
/// shared UBO binding points (<c>_Mtx</c>/binding 2, <c>ShpMtx</c>/binding 4) with no per-draw
/// instance addressing at all, so multi-actor rendering works by REBINDING a different actor's
/// own buffer immediately before that actor's own draw calls - exactly what a real engine does
/// between per-object draw calls - rather than trying to combine every actor into one buffer.
/// See <see cref="DeferredPipeline"/>'s remarks for the full reasoning.
/// </summary>
/// <remarks>
/// A group with a <see cref="Batch"/> is instead every placement of one model at once: its shapes
/// draw through their instanced programs (<see cref="InstancedShaderPatch"/>), once per visible run
/// rather than once per placement. Passes do not need to know which kind they hold - they bind with
/// <see cref="BindUbos"/> and draw with <see cref="Draw"/>.
/// </remarks>
public readonly record struct ActorDrawGroup(IReadOnlyList<UniformBlock> Uniforms, Vector4[] ModelMatrixRows, IReadOnlyList<LoadedShape> Shapes,
    InstanceBatch? Batch = null, bool ShadowRuns = false, int Cascade = -1)
{
    /// <summary>The batch's runs this group draws: a cascade's or the shadow focus's runs for a shadow group, its camera runs otherwise.</summary>
    List<(int First, int Count, int Lod)>? Runs => Batch is null ? null
        : ShadowRuns ? (Cascade >= 0 ? Batch.CascadeRuns(Cascade) : Batch.ShadowVisible) : Batch.Visible;

    /// <summary>
    /// Rebinds THIS actor's own bone-palette/ShpMtx buffers to their shared binding points - call
    /// immediately before drawing this group's shapes in any pass, and again for the next group
    /// before its own shapes. A batch binds its instance buffer instead, plus zeroed blocks at the
    /// two bindings for the fragment shaders that still declare them.
    /// </summary>
    /// <summary>What the group draws, for a profile: the model's name.</summary>
    public string Label => Batch is { } b ? Path.GetFileName(b.Model.DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : (Shapes.Count > 0 ? Shapes[0].Name : "actor");

    /// <summary>
    /// For a batch: binds only its instance buffer, when the zero blocks every batch shares are
    /// already bound - what changes from one batch's draw to the next in a pass sorted by program.
    /// </summary>
    public void BindInstanceBuffer(GLResourceCache resources) =>
        resources.Gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, InstancedShaderPatch.InstanceBinding, Batch!.Buffer);

    public void BindUbos(GLResourceCache resources)
    {
        if (Batch is { } batch)
        {
            Uniforms.Bind(resources);
            resources.Gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, InstancedShaderPatch.InstanceBinding, batch.Buffer);
            return;
        }
        Uniforms.Bind(resources);
    }

    /// <summary>Draws one of this group's shapes with the chosen program - the actor once, or every visible run of the batch.</summary>
    public void Draw(GL gl, ShaderProgramCache programs, LoadedShape shape, ShapeProgram which)
    {
        uint vao = which switch { ShapeProgram.ZOnly => shape.ZOnlyVao, ShapeProgram.Forward => shape.ForwardVao, _ => shape.GBufferVao };
        var samplers = which switch { ShapeProgram.ZOnly => shape.ZOnlySamplers, ShapeProgram.Forward => shape.ForwardSamplers, _ => shape.GBufferSamplers };

        if (Batch is not { } batch)
        {
            uint program = which switch { ShapeProgram.ZOnly => shape.ZOnlyProgram, ShapeProgram.Forward => shape.ForwardProgram, _ => shape.GBufferProgram };
            ShapeDrawing.Draw(gl, programs.Bindings.Material, program, vao, shape.MaterialUboBuffer, samplers, shape.IndexCount, shape.SamplerOverrides);
            return;
        }

        var runs = Runs!;
        if (runs.Count == 0)
            return;
        EnsureInstancedPrograms(programs, shape);
        uint instanced = which switch { ShapeProgram.ZOnly => shape.InstancedZOnlyProgram, ShapeProgram.Forward => shape.InstancedForwardProgram, _ => shape.InstancedGBufferProgram };
        ShapeDrawing.DrawInstanced(gl, programs.Bindings.Material, instanced, vao, shape, samplers, batch, runs);
    }

    /// <summary>Links a shape's instanced programs the first time it is drawn instanced.</summary>
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
