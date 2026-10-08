using WildRenderingSharp.Assets;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>Draws a host's terrain through the game's terrain programs: its G-buffer half, its water and its shadow.</summary>
public sealed class TerrainRenderer(FrameServices services, TerrainShading shading, LinearDepthPass linearDepth, DeferredScene scene)
{
    /// <summary><c>cTex_ColorBuffer</c>'s unit in the terrain water program.</summary>
    const int WaterColorBufferUnit = 19;

    uint _underDepthProgram;

    GL Gl => services.Gl;

    GLResourceCache Resources => services.Resources;

    public TerrainShading Shading => shading;

    public bool Available => shading.Available;

    /// <summary>
    /// The terrain's G-buffer half, after the actors': the linear depth and G-buffer under it are
    /// copied first, for the soft edge where the ground meets something set into it, then the host
    /// draws through the game's terrain program with a camera in the game's Y-up world.
    /// </summary>
    public void DrawGBuffer(ITerrainHost host, RenderTargets targets, Camera camera, CameraData terrainCamera)
    {
        linearDepth.Run(Resources, targets, camera.NearPlane, camera.FarPlane);
        var (underAlbedo, underNormal, underDepth) = targets.TerrainUnderCopies();

        // Nothing under the terrain is pushed out to effectively infinity, so a scene whose far plane
        // sits just past the ground does not fade the terrain into the empty G-buffer beneath it.
        _underDepthProgram = _underDepthProgram != 0 ? _underDepthProgram
            : GLProgramBuilder.Build(Gl, UnderDepthVertex, UnderDepthFragment, "terrain_under_depth");
        Gl.Disable(EnableCap.DepthTest);
        Gl.Disable(EnableCap.Blend);
        targets.BindColorTarget(underDepth);
        Gl.UseProgram(_underDepthProgram);
        Gl.BindTextureUniform(_underDepthProgram, "t", 0, targets.LinearDepth.Handle);
        Resources.DrawFullscreenTriangle();
        CopyGBufferLayer(targets, targets.GBuffer[1], underAlbedo);
        CopyGBufferLayer(targets, targets.GBuffer[3], underNormal);

        services.Profile.Camera(FrameUniformKeys.TerrainCamera, terrainCamera).Bind(Resources);
        Resources.BindMaterial(shading.MaterialBuffer);

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
        host.DrawGBuffer(new TerrainDraw(Gl, YUpWorld.PointBack(camera.Eye), -1, default));
        ClipOrigin.Game(Gl, false);

        ResetState();
        GLDiagnostics.CheckPass(Gl, "terrain");
    }

    /// <summary>
    /// The host's water, through the game's water program: drawn into the G-buffer over the lit
    /// opaque scene, or, when <paramref name="stamp"/>, marked in the pass-ID mask for <c>field_water</c>.
    /// </summary>
    public void DrawWater(ITerrainHost host, RenderTargets targets, Camera camera, bool stamp)
    {
        if (!shading.BindWater())
            return;

        Resources.BindCamera(FrameUniformKeys.TerrainCamera);
        Resources.BindEnvironment();
        Gl.Disable(EnableCap.CullFace);
        Gl.Disable(EnableCap.Blend);
        var draw = new TerrainDraw(Gl, YUpWorld.PointBack(camera.Eye), -1, default);
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
            var block = new float[8] { (index + 1) / 255f, camera.NearPlane, camera.FarPlane, 0f, 1f / targets.Width, 1f / targets.Height, 0f, 0f };
            Resources.Ubo("terrain_water_stamp", System.Runtime.InteropServices.MemoryMarshal.AsBytes(block.AsSpan()), bindingIndex: TotkBindings.TerrainWaterStamp);
            BindUnit(TerrainShading.StampDepthUnit, targets.GBufferDepth.Handle);
            host.DrawWater(draw, stamp: true);
        }

        ResetState();
        GLDiagnostics.CheckPass(Gl, stamp ? "terrain water stamp" : "terrain water");
    }

    /// <summary>Draws the terrain into one shadow cascade.</summary>
    public void DrawShadow(ITerrainHost host, int cascade, Camera camera, ShadowFocus focus, ShadowPass.LightMatrices light, CameraData sceneCamera)
    {
        services.Profile.Camera(FrameUniformKeys.TerrainLightCamera, CameraData.ForLight(light, sceneCamera)).Bind(Resources);
        host.DrawShadow(new TerrainDraw(Gl, YUpWorld.PointBack(camera.Eye), cascade,
            new Vector4(YUpWorld.PointBack(focus.Center), focus.Radius)));
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

    const string UnderDepthVertex = """
        #version 450 core
        out vec2 vUV;
        void main()
        {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    const string UnderDepthFragment = """
        #version 450 core
        uniform sampler2D t;
        in vec2 vUV;
        out float o;
        void main()
        {
            float d = texture(t, vUV).r;
            o = d >= 0.9999 ? 1.0e6 : d;
        }
        """;
}
