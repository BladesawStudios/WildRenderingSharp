using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// The reduced-resolution HDR target the clouds are drawn into, and the composite that blends it over the scene. The cloud program is
/// large and the dome covers most of the screen, so the cost is pure fill rate.
/// </summary>
sealed unsafe class CloudComposite(GL gl) : IDisposable
{
    static readonly string CompositeFrag = GlslFiles.Load("Totk/Sky/CloudDome/Composite.frag");

    uint _framebuffer, _texture, _program;
    int _width, _height;

    // Binds the target at the given size, cleared to transparent black, since the real blend happens once in Blend.
    public void Begin(int width, int height)
    {
        Resize(Math.Max(1, width), Math.Max(1, height));
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texture, 0);
        gl.Viewport(0, 0, (uint)_width, (uint)_height);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    // Blends the clouds over the scene with the game's blend.
    public void Blend(RenderTargets targets, GLResourceCache resources)
    {
        if (_program == 0)
            _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, CompositeFrag, "cloud_composite");

        targets.BindColorTarget(targets.Final);
        gl.Enable(EnableCap.Blend);
        gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.Zero, GLEnum.OneMinusSrcAlpha);
        // The equation is stated because an earlier pass may have left a different one set.
        gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        gl.UseProgram(_program);
        gl.BindTextureUniform(_program, "tCloud", 0, _texture);
        resources.DrawFullscreenTriangle();

        gl.Disable(EnableCap.Blend);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    public void Dispose()
    {
        gl.DeleteFramebuffer(_framebuffer);
        gl.DeleteTexture(_texture);
        if (_program != 0)
            gl.DeleteProgram(_program);
    }

    void Resize(int width, int height)
    {
        if (width == _width && height == _height && _texture != 0)
            return;
        if (_texture != 0)
            gl.DeleteTexture(_texture);
        if (_framebuffer == 0)
            _framebuffer = gl.GenFramebuffer();

        _width = width;
        _height = height;
        _texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.ClampToEdge);
    }
}
