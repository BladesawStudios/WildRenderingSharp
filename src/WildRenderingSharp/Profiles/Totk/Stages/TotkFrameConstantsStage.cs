using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>
/// Binds what the translated shaders expect regardless of the scene: the decompiler's support buffer, an empty storage buffer at
/// binding 0, a zeroed block for orphaned bindings, and neutral stand-ins for the vertex textures the game's engine renders itself.
/// </summary>
public sealed class TotkFrameConstantsStage : IFrameStage, IDisposable
{
    readonly FrameServices _services;
    readonly EngineVertexTextures _vertexTextures;
    readonly uint _zeroStorageBuffer;

    public TotkFrameConstantsStage(FrameServices services)
    {
        _services = services;
        _vertexTextures = new EngineVertexTextures(services.Gl);

        // Some translated shaders declare a storage block at binding 0 and never meaningfully use it.
        var gl = services.Gl;
        _zeroStorageBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _zeroStorageBuffer);
        gl.BufferData(BufferTargetARB.ShaderStorageBuffer, new byte[65536], BufferUsageARB.StaticDraw);
        BindZeroStorage();
    }

    public void Run(FrameContext frame)
    {
        // Rebound every frame: a host drawing between frames may use storage binding 0 itself.
        BindZeroStorage();
        _services.Resources.Ubo("support", SupportBufferUbo.Build(), bindingIndex: TotkBindings.Support);
        _services.Resources.BindZeroUbo(TotkBindings.Orphan, 65536);
        _vertexTextures.Bind();
    }

    void BindZeroStorage() =>
        _services.Gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _zeroStorageBuffer);

    public void Dispose()
    {
        _vertexTextures.Dispose();
        _services.Gl.DeleteBuffer(_zeroStorageBuffer);
    }
}
