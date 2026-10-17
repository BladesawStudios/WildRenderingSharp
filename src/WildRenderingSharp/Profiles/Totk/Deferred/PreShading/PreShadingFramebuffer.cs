using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>
/// The framebuffer the game's pre-shading passes draw into, with each buffer at the output location the game's programs write it to: PreShadow, PreFog,
/// PreMisc and, for the field pass, the ambient layer of the light pre-pass. Output 5, the premultiplied diffuse light, is not read by any resolve and
/// has no buffer.
/// </summary>
internal sealed class PreShadingFramebuffer(GL gl, bool writesAmbient) : IDisposable
{
    const int PreShadowLocation = 0, PreFogLocation = 1, AmbientLocation = 2, PreMiscLocation = 4, Locations = 6;

    readonly uint _framebuffer = gl.GenFramebuffer();

    public void Bind(RenderTargets targets)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        // Attached every time: deleting a texture detaches it from the framebuffer, and a resize deletes and makes them again, often under the same names.
        Attach(PreShadowLocation, targets.PreShadow);
        Attach(PreFogLocation, targets.PreFogHalf);
        Attach(PreMiscLocation, targets.PreMisc);
        if (writesAmbient)
            gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + AmbientLocation, targets.FieldLightPrePassArray.Handle, 0, 0);
        FramebufferCheck.Validate(gl, "game pre-shading");

        var buffers = new GLEnum[Locations];
        foreach (int location in writesAmbient ? new[] { PreShadowLocation, PreFogLocation, AmbientLocation, PreMiscLocation } : new[] { PreShadowLocation, PreFogLocation, PreMiscLocation })
            buffers[location] = GLEnum.ColorAttachment0 + location;
        gl.DrawBuffers(buffers);
        gl.Viewport(0, 0, (uint)targets.PreShadow.Width, (uint)targets.PreShadow.Height);
    }

    public void Dispose() => gl.DeleteFramebuffer(_framebuffer);

    void Attach(int location, GpuTexture texture) =>
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + location, TextureTarget.Texture2D, texture.Handle, 0);
}
