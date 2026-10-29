using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>The G-buffer's depth, kept and then written over on one side of a screen mask, so a draw lands only where the mask says.</summary>
/// <remarks>
/// <see cref="Keep"/> copies the depth as it stands; <see cref="Write"/> sets one side of the mask to the near plane (nothing drawn next lands there), the far
/// plane (whatever is drawn next wins there), the kept depth (to put it back) or another window depth where that is under one, and leaves the other side
/// alone. The G-buffer is left bound with its colour on.
/// </remarks>
internal sealed class MaskedDepth(GL gl) : IDisposable
{
    public enum Value { Near, Far, Kept, From }

    readonly uint _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, GlslFiles.Load("Totk/Deferred/EntranceInterior/PortalDepth.frag"),
        "entrance_PortalDepth");

    readonly ScreenTarget _kept = new(gl, InternalFormat.DepthComponent32f);

    public void Keep(RenderTargets targets)
    {
        _kept.Ensure(targets.Width, targets.Height);
        _kept.CopyFrom(targets.GBufferDepth.Handle);
    }

    // mask is a screen-sized texture set where its red is over a half; inside picks the side of it that is written. from is the window depth Value.From
    // writes, in red.
    public void Write(GLResourceCache resources, RenderTargets targets, uint mask, bool inside, Value value, uint from = 0)
    {
        targets.BindGBuffer();
        targets.SetGBufferColorMask(false);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Always);
        gl.DepthMask(true);
        gl.UseProgram(_program);
        gl.BindTextureUniform(_program, "uMask", 0, mask);
        gl.BindTextureUniform(_program, "uSavedDepth", 1, _kept.Texture);
        gl.BindTextureUniform(_program, "uFrom", 2, from != 0 ? from : _kept.Texture);
        gl.SetInt(_program, "uInside", inside ? 1 : 0);
        gl.SetInt(_program, "uValue", (int)value);
        resources.DrawFullscreenTriangle();
        targets.SetGBufferColorMask(true);
    }

    public void Dispose()
    {
        gl.ReleaseProgram(_program);
        _kept.Dispose();
    }
}
