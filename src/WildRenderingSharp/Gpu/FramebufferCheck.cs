using Silk.NET.OpenGL;
using WildRenderingSharp.Logging;

namespace WildRenderingSharp.Gpu;

/// <summary>Reports an incomplete framebuffer by name when it is built, since an incomplete one silently discards every draw and clear into it.</summary>
static class FramebufferCheck
{
    // Checked once per construction because the status query can force a driver sync.
    public static void Validate(GL gl, string name)
    {
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Log.Error($"[RenderTargets] {name} framebuffer is INCOMPLETE ({status}) - every draw into it will be discarded.");
    }
}
