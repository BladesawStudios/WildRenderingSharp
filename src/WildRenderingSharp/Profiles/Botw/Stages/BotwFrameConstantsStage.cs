using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>Binds what the translated shaders expect whatever the scene: the decompiler's support buffer and an empty storage buffer.</summary>
public sealed class BotwFrameConstantsStage : IFrameStage, IDisposable
{
    readonly FrameServices _services;
    readonly uint _zeroStorageBuffer;

    public BotwFrameConstantsStage(FrameServices services)
    {
        _services = services;
        var gl = services.Gl;
        _zeroStorageBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _zeroStorageBuffer);
        gl.BufferData(BufferTargetARB.ShaderStorageBuffer, new byte[65536], BufferUsageARB.StaticDraw);
    }

    public void Run(FrameContext frame)
    {
        _services.Gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _zeroStorageBuffer);
        _services.Resources.Ubo("support", SupportBufferUbo.Build(), bindingIndex: BotwBindings.Support);
    }

    public void Dispose() => _services.Gl.DeleteBuffer(_zeroStorageBuffer);
}
