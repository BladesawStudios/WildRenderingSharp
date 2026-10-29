using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>Draws the hall a shrine's entrance shows through its doorway, there and nowhere else, as a portal.</summary>
/// <remarks>
/// The entrance actor draws a sub-model besides its rock: the shrine's whole entrance hall, which reaches some two hundred metres back into the ground behind the
/// rock and is left out of every ordinary pass (<see cref="LoadedShape.PortalOnly"/>). The game draws it in its DungeonEntrance/Internal queues, scissored to the
/// entrance, with its depth program writing a depth of zero so that it beats whatever stands where it is seen. This runs once the G-buffer is filled, terrain
/// included. Where the doorway is (<see cref="DoorwayMasks"/>), the rock is left out, which is drawn here and not by the G-buffer pass, front faces only as the
/// game draws it; so through the doorway the hall is seen from outside and the world from inside. Where the hall is seen the depth goes to the far plane and
/// everywhere else to the near one, the hall is drawn, and the depth elsewhere is put back as it was. The pass-ID mask is then stamped with the entrance's pass
/// where the hall is seen (<see cref="Stamp"/>), and how far each surface lies behind where the view entered the rock is given to its haze as
/// <see cref="Thickness"/>.
/// </remarks>
internal sealed class EntranceInterior(GL gl) : IDisposable
{
    readonly uint _stampProgram = Program(gl, "PortalStamp"), _thicknessProgram = Program(gl, "Thickness");

    // The texture transform the hall's copy of the rock reads its cut mask through, and the floats of it that move the mask down (the translation's second).
    const string MaskTransform = "p_tex_srt2";
    const int MaskTranslationY = 5;

    // TODO: the copy's mesh has its mask coordinates two higher than the rock's own, which the mask's clamped edge turns into a strip where the rock's is an arch.
    // The two is read off the A hall's mesh; what would remove it is the game's own reason the two meshes' masks agree.
    const float MaskMeshOffset = 2f;

    readonly DoorwayMasks _masks = new(gl);
    readonly ScreenTarget _thickness = new(gl, InternalFormat.R16f);
    readonly MaskedDepth _depth = new(gl), _hallDepth = new(gl);

    static uint Program(GL gl, string name) =>
        GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, GlslFiles.Load($"Totk/Deferred/EntranceInterior/{name}.frag"), $"entrance_{name}");

    // cTex_LocalReflection for the entrance's pre-shading pass, or 0 when no entrance was drawn this frame.
    public uint Thickness { get; private set; }

    // Where a shrine's doorway is seen into this frame, red 1 there, as the G-buffer is laid out; 0 when no entrance was drawn.
    public uint Portal => _drawn ? _masks.Portal : 0;

    bool _drawn;

    public void Run(GLResourceCache resources, RenderTargets targets, FrameContext frame, IGameProfile profile, ShapeDrawer drawer,
        GBufferControlInputs control)
    {
        Thickness = 0;
        _drawn = false;
        var hulls = frame.Setup.OpaqueGroups
            .Select(g => g with { Shapes = g.Shapes.Where(s => s.IsEntranceRock() && s.CullsFaces && s.HasZOnly).ToList() })
            .Where(g => g.Shapes.Count > 0).ToList();
        if (hulls.Count == 0)
            return;

        resources.BindCamera(FrameUniformKeys.GBufferCamera);
        ClipOrigin.Game(gl, true);
        _masks.Ensure(targets.Width, targets.Height);
        _thickness.Ensure(targets.Width, targets.Height);
        float near = frame.Camera.NearPlane, far = frame.Camera.FarPlane;

        var panels = PlacedShapes.Of(frame, profile, s => s.IsDoorwayPanel() && s.HasZOnly);
        _masks.Draw(resources, hulls, panels, drawer, control);
        _masks.Find(resources, targets, DoorwayMasks.Mask.Cut, near, far);

        _depth.Keep(targets);
        _depth.Write(resources, targets, _masks.Cut, inside: true, MaskedDepth.Value.Near);
        DrawRock(resources, targets, hulls, drawer);
        _depth.Write(resources, targets, _masks.Cut, inside: true, MaskedDepth.Value.Kept);
        _masks.Find(resources, targets, DoorwayMasks.Mask.Portal, near, far);
        _masks.Find(resources, targets, DoorwayMasks.Mask.Clip, near, far);

        _depth.Keep(targets);
        _depth.Write(resources, targets, _masks.Portal, inside: true, MaskedDepth.Value.Far);
        _depth.Write(resources, targets, _masks.Portal, inside: false, MaskedDepth.Value.Near);
        // A see-through shape the game lays over the G-buffer from the scene's colour, as the ceiling's barrier is, is drawn over the lit hall by the pass that
        // draws such shapes, through ThroughDoorway.
        DrawHall(resources, targets, PlacedShapes.Of(frame, profile, s => s.IsSeenThroughDoorway() && !s.Blend && !s.ReadsSceneColor && s.RenderState.DepthWriteEnabled
            && s.Tags.GetValueOrDefault("option.gsys_gbuffer_xlu") != "True"), drawer, control);
        _depth.Write(resources, targets, _masks.Portal, inside: false, MaskedDepth.Value.Kept);
        DrawFire(resources, targets, panels, drawer);

        MeasureThickness(resources, targets, near, far);
        Thickness = _thickness.Texture;
        // The game then draws the rock's depth again over the hall, so everything after sees the rock's front there, the field's haze above all; the hall's
        // own depth is kept for what is drawn into it later.
        _hallDepth.Keep(targets);
        _depth.Write(resources, targets, _masks.Portal, inside: true, MaskedDepth.Value.From, _masks.Front);
        _drawn = true;

        ClipOrigin.Game(gl, false);
        FaceCulling.Reset(gl);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Less);
        gl.DepthMask(true);
    }

    // Runs draw into the G-buffer with everything outside the doorway shut to it, then puts the depth back; for the see-through shapes of the hall, drawn after
    // the frame is lit. The clip origin and camera are the caller's.
    public void ThroughDoorway(GLResourceCache resources, RenderTargets targets, Action draw)
    {
        if (!_drawn)
            return;
        _depth.Keep(targets);
        _hallDepth.Write(resources, targets, _masks.Portal, inside: true, MaskedDepth.Value.Kept);
        _depth.Write(resources, targets, _masks.Portal, inside: false, MaskedDepth.Value.Near);
        draw();
        _depth.Write(resources, targets, _masks.Portal, inside: true, MaskedDepth.Value.Kept);
        _depth.Write(resources, targets, _masks.Portal, inside: false, MaskedDepth.Value.Kept);
        gl.DepthFunc(DepthFunction.Less);
    }

    // Stamps the entrance's pass over the doorway in the pass-ID mask, which is bound; the hall is the entrance's, whatever its own materials are named for.
    public void Stamp(GLResourceCache resources, int passIndex)
    {
        if (!_drawn || passIndex < 0)
            return;
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.UseProgram(_stampProgram);
        gl.BindTextureUniform(_stampProgram, "uPortal", 0, _masks.Portal);
        gl.SetFloat(_stampProgram, "uPassId", (passIndex + 1) / 255f);
        resources.DrawFullscreenTriangle();
    }

    // The rock into the G-buffer, as the G-buffer pass leaves it to this (LoadedShape.ProfileDrawsGBuffer): its front faces, the only ones its material
    // shows. Its back faces, seen from inside it, would show the fire its dissolve burns where the game shows the hall.
    void DrawRock(GLResourceCache resources, RenderTargets targets, List<ActorDrawGroup> hulls, ShapeDrawer drawer)
    {
        targets.BindGBuffer();
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Less);
        gl.DepthMask(true);
        gl.Enable(EnableCap.CullFace);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.CullFace(TriangleFace.Back);
        Draw(resources, hulls, drawer, ShapeProgram.GBuffer);
    }

    // The doorway's fire, over the rock and the hall's edge as the game layers it: the panel writes no depth, so drawn with the G-buffer pass it was hidden by
    // the rock drawn here after it. It shows either side.
    void DrawFire(GLResourceCache resources, RenderTargets targets, List<ActorDrawGroup> panels, ShapeDrawer drawer)
    {
        targets.BindGBuffer();
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.DepthMask(false);
        gl.Disable(EnableCap.CullFace);
        Draw(resources, panels, drawer, ShapeProgram.GBuffer);
        gl.DepthMask(true);
    }

    // The hall's own G-buffer programs clip it as the game's do, by what is bound for them (GBufferControlInputs.WithHallClip).
    void DrawHall(GLResourceCache resources, RenderTargets targets, List<ActorDrawGroup> hall, ShapeDrawer drawer, GBufferControlInputs control) =>
        control.WithHallClip(_masks.Clip, () => DrawHallShapes(resources, targets, hall, drawer));

    void DrawHallShapes(GLResourceCache resources, RenderTargets targets, List<ActorDrawGroup> hall, ShapeDrawer drawer)
    {
        targets.BindGBuffer();
        gl.DepthFunc(DepthFunction.Less);
        gl.DepthMask(true);
        foreach (var group in hall)
        {
            group.BindUbos(resources);
            foreach (LoadedShape shape in group.Shapes)
            {
                // The faces the material shows, as the game draws them: the hall's copy of the rock faces into the hall, and its back would stand in the doorway.
                FaceCulling.Apply(gl, shape.RenderState);
                if (!shape.IsHallRockCopy() || !MaterialOverride.Draw(shape, MaskMovedDown, () => group.Draw(drawer, shape, ShapeProgram.GBuffer)))
                    group.Draw(drawer, shape, ShapeProgram.GBuffer);
            }
        }
        FaceCulling.Reset(gl);
    }

    // The hall's copy of the rock with the coordinates of its cut mask moved back down by what its mesh is offset by.
    static bool MaskMovedDown(MaterialOverride material) =>
        material.TryGet(MaskTransform, out float shift, MaskTranslationY) && material.TrySet(MaskTransform, shift - MaskMeshOffset, MaskTranslationY);

    void MeasureThickness(GLResourceCache resources, RenderTargets targets, float near, float far)
    {
        _thickness.BindCleared(0f);
        gl.Disable(EnableCap.DepthTest);
        gl.UseProgram(_thicknessProgram);
        gl.BindTextureUniform(_thicknessProgram, "uDoorDepth", 0, _masks.Front);
        gl.BindTextureUniform(_thicknessProgram, "uSceneDepth", 1, targets.GBufferDepth.Handle);
        gl.SetFloat(_thicknessProgram, "uNear", near);
        gl.SetFloat(_thicknessProgram, "uFar", far);
        resources.DrawFullscreenTriangle();
    }

    static void Draw(GLResourceCache resources, List<ActorDrawGroup> groups, ShapeDrawer drawer, ShapeProgram which)
    {
        foreach (var group in groups)
        {
            group.BindUbos(resources);
            foreach (LoadedShape shape in group.Shapes)
                group.Draw(drawer, shape, which);
        }
    }

    public void Dispose()
    {
        foreach (uint program in new[] { _stampProgram, _thicknessProgram })
            gl.ReleaseProgram(program);
        _masks.Dispose();
        _thickness.Dispose();
        _depth.Dispose();
        _hallDepth.Dispose();
    }
}
