using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Resources;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>Binds what the decompiled shaders expect whatever the scene.</summary>
public sealed class BotwFrameConstantsStage(StageServices services) : IFrameStage, IDisposable
{
    readonly DecompilerBindings _decompiler = new(services.Gl, services.Resources);

    public void Run(FrameContext frame) => _decompiler.Bind();

    public void Dispose() => _decompiler.Dispose();
}
