using KingRenderingSharp.Graph.Pipeline.Frame;
using KingRenderingSharp.Graph.Pipeline.Resources;

namespace KingRenderingSharp.Games.Botw.Stages;

/// <summary>Binds what the decompiled shaders expect whatever the scene.</summary>
internal sealed class BotwFrameConstantsStage(StageServices services) : IFrameStage, IDisposable
{
    readonly DecompilerBindings _decompiler = new(services.Gl, services.Resources);

    public void Run(FrameContext frame) => _decompiler.Bind();

    public void Dispose() => _decompiler.Dispose();
}
