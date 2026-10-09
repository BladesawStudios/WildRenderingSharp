using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>
/// Binds what the decompiled shaders expect whatever the scene, plus a zeroed block for orphaned bindings and neutral stand-ins for
/// the vertex textures the game's engine renders itself.
/// </summary>
public sealed class TotkFrameConstantsStage(StageServices services) : IFrameStage, IDisposable
{
    readonly DecompilerBindings _decompiler = new(services.Gl, services.Resources);
    readonly EngineVertexTextures _vertexTextures = new(services.Gl);

    public void Run(FrameContext frame)
    {
        _decompiler.Bind();
        services.Resources.BindZeroed(TotkBlocks.Orphan);
        _vertexTextures.Bind();

        // Uploaded without a binding: the resolve pass binds it itself, in place of the scene camera, for the passes that tile the screen.
        services.Resources.Upload(TotkCameraUniforms.BuildField(frame.Cam));
    }

    public void Dispose()
    {
        _vertexTextures.Dispose();
        _decompiler.Dispose();
    }
}
