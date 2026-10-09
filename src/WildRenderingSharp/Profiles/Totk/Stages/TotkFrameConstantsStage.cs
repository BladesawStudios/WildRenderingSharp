using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>
/// Binds what the decompiled shaders expect whatever the scene, plus a zeroed block for orphaned bindings and neutral stand-ins for
/// the vertex textures the game's engine renders itself.
/// </summary>
public sealed class TotkFrameConstantsStage(StageServices services) : IFrameStage, IDisposable
{
    const int OrphanBlockBytes = 65536;

    readonly DecompilerBindings _decompiler = new(services.Gl, services.Resources);
    readonly EngineVertexTextures _vertexTextures = new(services.Gl);

    public void Run(FrameContext frame)
    {
        _decompiler.Bind();
        services.Resources.BindZeroUbo(TotkBindings.Orphan, OrphanBlockBytes);
        _vertexTextures.Bind();

        // Uploaded without a binding: the resolve pass binds it itself, in place of the scene camera, for the passes that tile the screen.
        var field = TotkCameraUniforms.BuildField(frame.Cam);
        services.Resources.Ubo(field.Key, field.Data!);
    }

    public void Dispose()
    {
        _vertexTextures.Dispose();
        _decompiler.Dispose();
    }
}
