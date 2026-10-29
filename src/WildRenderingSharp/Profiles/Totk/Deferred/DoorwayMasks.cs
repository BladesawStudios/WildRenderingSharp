using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>Where a shrine entrance's doorway is on screen: where it cuts the rock away, and where the hall is seen through it.</summary>
/// <remarks>
/// The rock's hull and the doorway's panel (<c>Mt_WarpHole_BackAura_00</c>, the panel the green fire burns around) are drawn by their depth programs. The
/// hull is drawn whole, its front faces and back faces apart, which says where the eye is inside it. The panel is drawn as far as its fire's outer edge, as
/// the rock's own alpha test cuts almost nothing there, and its depth is kept so that rock standing in front of it hides it. See PortalMask.frag for how they
/// combine.
/// </remarks>
internal sealed class DoorwayMasks(GL gl) : IDisposable
{
    readonly uint _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, GlslFiles.Load("Totk/Deferred/EntranceInterior/PortalMask.frag"),
        "entrance_PortalMask");

    // The panel's dissolve: a value x, less the offset, burns as fire between an outer edge (x just above zero) and an inner one (x times the inner edge's
    // steepness reaching one half), and the opening is inside the inner edge. With the inner edge gone and the offset moved out to where it was, what the
    // panel's alpha test keeps is the opening alone, and the fire the panel draws stays outside it.
    const string Offset = "p_const_value4", InnerEdge = "p_const_value7";

    readonly ScreenTarget _front = new(gl, InternalFormat.R32f), _back = new(gl, InternalFormat.R32f), _panel = new(gl, InternalFormat.DepthComponent32f),
        _doorway = new(gl, InternalFormat.RG8), _cut = new(gl, InternalFormat.R8), _portal = new(gl, InternalFormat.R8), _clip = new(gl, InternalFormat.R32f);

    // What Find makes: where the doorway cuts the rock away, where the hall is seen, or the depth the hall is clipped at.
    public enum Mask { Cut, Portal, Clip }

    // Window depth of the nearest front face of the hulls, 1 where none is.
    public uint Front => _front.Texture;

    // Red 1 where the doorway cuts the rock away, once Find has made Mask.Cut.
    public uint Cut => _cut.Texture;

    // Red 1 where the hall is seen, once Find has made Mask.Portal.
    public uint Portal => _portal.Texture;

    // The window depth in front of which the hall's own programs discard it (what they read as their unit 10), once Find has made Mask.Clip: the rock's
    // front where the hall is seen through the doorway, so that nothing of the hall in front of the rock shows, and zero elsewhere.
    public uint Clip => _clip.Texture;

    public void Ensure(int width, int height)
    {
        foreach (var target in Targets)
            target.Ensure(width, height);
        _doorway.AttachDepth(_panel);
    }

    // The hulls' faces by depth, then where the panel lands; the G-buffer camera and the game's clip origin are the caller's.
    public void Draw(GLResourceCache resources, List<ActorDrawGroup> hulls, List<ActorDrawGroup> panels, ShapeDrawer drawer, GBufferControlInputs control)
    {
        control.BindDepthOutput();
        DrawFaces(resources, hulls, drawer, _front, TriangleFace.Back);
        DrawFaces(resources, hulls, drawer, _back, TriangleFace.Front);
        control.Bind();
        DrawDoorway(resources, panels, drawer);
    }

    // One of the masks; see PortalMask.frag.
    public void Find(GLResourceCache resources, RenderTargets targets, Mask mask, float near, float far)
    {
        (mask switch { Mask.Cut => _cut, Mask.Portal => _portal, _ => _clip }).BindCleared(0f);
        gl.Disable(EnableCap.DepthTest);
        gl.UseProgram(_program);
        gl.BindTextureUniform(_program, "uFront", 0, _front.Texture);
        gl.BindTextureUniform(_program, "uBack", 1, _back.Texture);
        gl.BindTextureUniform(_program, "uPanel", 2, _panel.Texture);
        gl.BindTextureUniform(_program, "uDoorway", 3, _doorway.Texture);
        gl.BindTextureUniform(_program, "uSceneDepth", 4, targets.GBufferDepth.Handle);
        gl.SetInt(_program, "uMask", (int)mask);
        gl.SetFloat(_program, "uNear", near);
        gl.SetFloat(_program, "uFar", far);
        resources.DrawFullscreenTriangle();
    }

    // The nearest of the hulls' faces that the culling leaves, drawn whole by the depth program's mode that gives the depth as its colour, whatever is in
    // front of them.
    void DrawFaces(GLResourceCache resources, List<ActorDrawGroup> groups, ShapeDrawer drawer, ScreenTarget target, TriangleFace culled)
    {
        target.BindCleared(1f);
        gl.Disable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        gl.BlendEquation(BlendEquationModeEXT.Min);
        gl.Enable(EnableCap.CullFace);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.CullFace(culled);
        foreach (var group in groups)
        {
            group.BindUbos(resources);
            foreach (LoadedShape shape in group.Shapes)
                group.Draw(drawer, shape, ShapeProgram.ZOnly);
        }
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
    }

    // Wherever the doorway lands, its front in red, with its depth, and either side in green, the constant colour written whatever the program's own is.
    void DrawDoorway(GLResourceCache resources, List<ActorDrawGroup> panels, ShapeDrawer drawer)
    {
        _doorway.BindCleared(0f);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Less);
        gl.DepthMask(true);
        gl.Enable(EnableCap.Blend);
        gl.BlendColor(1f, 1f, 1f, 1f);
        gl.BlendFunc(BlendingFactor.ConstantColor, BlendingFactor.Zero);
        gl.Enable(EnableCap.CullFace);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.CullFace(TriangleFace.Back);
        gl.ColorMask(true, false, false, false);
        DrawPanels(resources, panels, drawer);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.ColorMask(false, true, false, false);
        DrawPanels(resources, panels, drawer);
        gl.ColorMask(true, true, true, true);
        gl.Disable(EnableCap.Blend);
    }

    // The panel's opening, its alpha test keeping only that for the draw (see InnerEdge).
    void DrawPanels(GLResourceCache resources, List<ActorDrawGroup> panels, ShapeDrawer drawer)
    {
        foreach (var group in panels)
        {
            group.BindUbos(resources);
            foreach (LoadedShape shape in group.Shapes)
                MaterialOverride.Draw(shape, OpeningOnly, () => group.Draw(drawer, shape, ShapeProgram.ZOnly));
        }
    }

    static bool OpeningOnly(MaterialOverride material) =>
        material.TryGet(InnerEdge, out float steepness) && steepness > 0f && material.TryGet(Offset, out float offset)
        && material.TrySet(Offset, offset + 0.5f / steepness) && material.TrySet(InnerEdge, 0f);

    ScreenTarget[] Targets => [_front, _back, _panel, _doorway, _cut, _portal, _clip];

    public void Dispose()
    {
        gl.ReleaseProgram(_program);
        foreach (var target in Targets)
            target.Dispose();
    }
}
