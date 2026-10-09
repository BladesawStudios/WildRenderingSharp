using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering.Cameras;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>Draws a host's terrain through the game's terrain programs: its G-buffer half, its water and its shadow.</summary>
internal sealed class TerrainRenderer(StageServices services, TerrainShading shading, LinearDepthPass linearDepth, DeferredScene scene)
{
    const int WaterColorBufferUnit = 19;

    uint _underDepthProgram;

    GL Gl => services.Gl;

    GLResourceCache Resources => services.Resources;

    public TerrainShading Shading => shading;

    public bool Available => shading.Available;

    // Whether this frame's G-buffer holds the host's terrain, which the resolve stage lights apart from the rest.
    public bool GBufferDrawn { get; private set; }

    public void BeginFrame() => GBufferDrawn = false;

    public void DrawGBuffer(ITerrainHost host, RenderTargets targets, Camera camera, CameraData terrainCamera)
    {
        linearDepth.Run(Resources, targets, camera.NearPlane, camera.FarPlane);
        var (underAlbedo, underNormal, underDepth) = targets.TerrainUnderCopies();

        // Nothing under the terrain is pushed out to effectively infinity, so a scene whose far plane
        // sits just past the ground does not fade the terrain into the empty G-buffer beneath it.
        _underDepthProgram = _underDepthProgram != 0 ? _underDepthProgram
            : GLProgramBuilder.Build(Gl, FullscreenShaders.Vertex450, UnderDepthFragment, "terrain_under_depth");
        Gl.Disable(EnableCap.DepthTest);
        Gl.Disable(EnableCap.Blend);
        targets.BindColorTarget(underDepth);
        Gl.UseProgram(_underDepthProgram);
        Gl.BindTextureUniform(_underDepthProgram, "t", 0, targets.LinearDepth.Handle);
        Resources.DrawFullscreenTriangle();
        CopyGBufferLayer(targets, targets.GBuffer[1], underAlbedo);
        CopyGBufferLayer(targets, targets.GBuffer[3], underNormal);

        Resources.Bind(services.Profile.Camera(FrameUniformKeys.TerrainCamera, terrainCamera));
        Resources.BindMaterial(shading.Material);

        targets.BindGBuffer();
        targets.SetGBufferColorMask(true);
        Gl.Disable(EnableCap.Blend);
        Gl.Disable(EnableCap.CullFace);
        Gl.Enable(EnableCap.DepthTest);
        Gl.DepthFunc(DepthFunction.Less);
        Gl.DepthMask(true);
        BindUnit(0, underAlbedo.Handle);
        BindUnit(1, underNormal.Handle);
        BindUnit(4, underDepth.Handle);

        ClipOrigin.Game(Gl, true);
        host.DrawGBuffer(new TerrainDraw(Gl, camera.Eye, -1, default));
        ClipOrigin.Game(Gl, false);

        ResetState();
        GLDiagnostics.CheckPass(Gl, "terrain");
        GBufferDrawn = true;
    }

    public void DrawWater(ITerrainHost host, RenderTargets targets, Camera camera, bool stamp)
    {
        if (!shading.BindWater())
            return;

        Resources.BindCamera(FrameUniformKeys.TerrainCamera);
        Resources.BindEnvironment();
        Gl.Disable(EnableCap.CullFace);
        Gl.Disable(EnableCap.Blend);
        var draw = new TerrainDraw(Gl, camera.Eye, -1, default);
        if (!stamp)
        {
            targets.BindGBuffer();
            Gl.Enable(EnableCap.DepthTest);
            Gl.DepthFunc(DepthFunction.Lequal);
            Gl.DepthMask(true);
            BindUnit(SceneColorShapePass.MaterialIdUnit, targets.MaterialIdCopy.Handle);
            BindUnit(SceneColorShapePass.LinearDepthHalfUnit, targets.LinearDepthHalf.Handle);
            BindUnit(WaterColorBufferUnit, targets.Behind.Handle);
            ClipOrigin.Game(Gl, true);
            host.DrawWater(draw, stamp: false);
            ClipOrigin.Game(Gl, false);
            Gl.DepthFunc(DepthFunction.Less);
        }
        else
        {
            // Drawn the right way up, as the mask is, against the G-buffer's depth.
            targets.BindPassIdTarget();
            Gl.Disable(EnableCap.DepthTest);
            int index = scene.PassIndex(DeferredScene.WaterPass);
            var block = new UboWriter(TotkBlocks.WaterStamp);
            block.Set(0, (index + 1) / 255f, camera.NearPlane, camera.FarPlane, 0f);
            block.Set(1, 1f / targets.Width, 1f / targets.Height, 0f, 0f);
            Resources.Bind(block.ToUbo("terrain_water_stamp"));
            BindUnit(TerrainShading.StampDepthUnit, targets.GBufferDepth.Handle);
            host.DrawWater(draw, stamp: true);
        }

        ResetState();
        GLDiagnostics.CheckPass(Gl, stamp ? "terrain water stamp" : "terrain water");
    }

    public void DrawShadow(ITerrainHost host, int cascade, Camera camera, ShadowFocus focus, ShadowPass.LightMatrices light, CameraData sceneCamera)
    {
        Resources.Bind(services.Profile.Camera(FrameUniformKeys.TerrainLightCamera, sceneCamera.ForLight(light.View, light.Proj)));
        host.DrawShadow(new TerrainDraw(Gl, camera.Eye, cascade, new Vector4(focus.Center, focus.Radius)));
        Gl.UseProgram(0);
        Gl.BindVertexArray(0);
    }

    void CopyGBufferLayer(RenderTargets targets, GpuTexture source, GpuTexture destination) =>
        Gl.CopyImageSubData(source.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            destination.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)targets.Width, (uint)targets.Height, 1);

    void ResetState()
    {
        Gl.UseProgram(0);
        Gl.BindVertexArray(0);
        Gl.ActiveTexture(TextureUnit.Texture0);
        Gl.Disable(EnableCap.DepthTest);
    }

    void BindUnit(int unit, uint handle)
    {
        Gl.ActiveTexture(TextureUnit.Texture0 + unit);
        Gl.BindTexture(TextureTarget.Texture2D, handle);
    }

    static readonly string UnderDepthFragment = GlslFiles.Load("Totk/Terrain/TerrainRenderer/UnderDepth.frag");
}
