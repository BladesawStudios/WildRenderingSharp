using System.Diagnostics;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Profiles.Totk.Sky.Clouds;

/// <summary>
/// Draws the game's own <c>agl_cloud</c> program on the procedural cloud dome (<see cref="CloudDomeMesh"/>, ported from
/// <c>Cloud::initVertex_</c>).
/// </summary>
public sealed unsafe class CloudDomePass : IDisposable
{
    // Drawn at the dome's true size, which the shader's distance fades are calibrated against.
    const float DomeScale = 1f;

    readonly GL _gl;
    readonly uint _program;
    readonly uint _vao, _vbo, _ibo;
    readonly int _indexCount;
    readonly CloudMasks? _masks;
    readonly CloudComposite? _composite;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    // Holds an already time-integrated offset, not the raw scroll speed, as the vertex shader expects.
    float _animSeconds;
    double _animBase;
    bool _dumped;

    public CloudDomePass(GL gl, ShaderProgramCache programs, string? systemTexturesDirectory = null)
    {
        _gl = gl;
        if (!programs.Exists("agl_cloud"))
            return;

        _program = programs.Load("agl_cloud", patchVertex: CloudDistanceFade.PatchVertex, patchFragment: CloudDistanceFade.PatchFragment);
        LinkBlocksAndSamplers();

        // A unit dome: the game folds the world scale into the matrices it uploads, so scaling the mesh too would apply it twice.
        Vector3[] vertices = CloudDomeMesh.BuildVertices(CloudDomeMesh.NearRings);
        int[] indices = CloudDomeMesh.BuildIndices(CloudDomeMesh.NearRings);
        _indexCount = indices.Length;
        (_vao, _vbo, _ibo) = UploadMesh(vertices, indices);

        _masks = new CloudMasks(gl, systemTexturesDirectory);
        _composite = new CloudComposite(gl);
    }

    // One cloud layer to draw: its resolved parameters and the palette colours that shade it.
    public readonly record struct Layer(CloudPostFxLayer Params, EnvPalette.CloudLayer Colours);

    internal static EnvPalette.CloudLayer FallbackCloudLayer(CloudPostFxLayer layer) => new(
        Present: true,
        BacklightPower: layer.BacklightPower,
        ColorBackLight: layer.BacklightColor,
        ColorBase: layer.BaseColor,
        ColorHilight: layer.HilightColor,
        ColorShadow: layer.ShadowColor,
        IntensityBase: layer.BaseColorIntensity,
        IntensityHilight: layer.HilightColorIntensity,
        IntensityShadow: layer.ShadowColorIntensity);

    // Moves the cloud clock on (or holds it) and returns the seconds every layer's offsets and weather values are evaluated at.
    public float Advance(bool animate)
    {
        double now = _clock.Elapsed.TotalSeconds;
        // Measured against wall-clock time so the offset is right whenever a frame is drawn (the viewport only redraws on input).
        if (animate)
            _animSeconds = (float)(now - _animBase);
        else
            _animBase = now - _animSeconds;
        return _animSeconds;
    }

    // Draws the layers in the order given, which should be far to near.
    public void Run(GLResourceCache resources, RenderTargets targets, EnvPalette palette,
        CloudPostFxShared shared, IReadOnlyList<Layer> layers, float seconds,
        in CameraData camera, Vector3 cameraEye, Vector3 sunWorld,
        float brightness, float exposure, CloudFadeSettings fade, Vector3 skyColor, float resolutionScale, uint scatterTexture)
    {
        if (_program == 0)
            return;
        foreach (var layer in layers)
            DrawLayer(resources, targets, palette, shared, layer, seconds, camera, cameraEye, sunWorld,
                brightness, exposure, fade, skyColor, resolutionScale, scatterTexture);
    }

    public void Dispose()
    {
        if (_program == 0)
            return;
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ibo);
        _masks!.Dispose();
        _composite!.Dispose();
    }

    // The shader's per-stage block indices collide across stages, so each block is rebound explicitly.
    void LinkBlocksAndSamplers()
    {
        bool fragCommon = _gl.BindUniformBlock(_program, "_fp_c3", CloudBlocks.Common.Binding);
        bool vertCommon = _gl.BindUniformBlock(_program, "_vp_c4", CloudBlocks.Common.Binding);
        bool vertView = _gl.BindUniformBlock(_program, "_vp_c3", CloudBlocks.View.Binding);
        Console.WriteLine($"[CloudDomePass] real agl_cloud linked - uniform blocks rebound: " +
            $"Common(frag)={fragCommon}, Common(vert)={vertCommon}, View(vert)={vertView}");

        _gl.UseProgram(_program);
        _gl.SetSamplerUnit(_program, "fp_t_tcb_A", 0); // cBaseTexture
        _gl.SetSamplerUnit(_program, "fp_t_tcb_E", 1); // cBaseTexture_Blend
        _gl.SetSamplerUnit(_program, "fp_t_tcb_8", 2); // cNoiseTexture
        _gl.SetSamplerUnit(_program, "fp_t_tcb_C", 3); // cNoiseTexture_Blend
        _gl.SetSamplerUnit(_program, "vp_t_tcb_8", 4); // cScatterTexture (vertex stage)
    }

    (uint Vao, uint Vbo, uint Ibo) UploadMesh(Vector3[] vertices, int[] indices)
    {
        uint vao = _gl.GenVertexArray(), vbo = _gl.GenBuffer(), ibo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (Vector3* p = vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(Vector3)), p, BufferUsageARB.StaticDraw);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Vector3), (void*)0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);
        fixed (int* p = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(int)), p, BufferUsageARB.StaticDraw);
        _gl.BindVertexArray(0);
        return (vao, vbo, ibo);
    }

    // The camera's projection with its far plane pushed out to at least wantedFar, so the dome is not clipped.
    static Matrix4x4 WidenFarPlane(in CameraData camera, float wantedFar) =>
        Matrix4x4.CreatePerspectiveFieldOfView(2f * MathF.Atan(camera.TanHalfFovY), camera.Aspect, camera.Near, MathF.Max(wantedFar, camera.Far))
        * ClipSpace.ZeroToOneDepthToGl;

    void DrawLayer(GLResourceCache resources, RenderTargets targets, EnvPalette palette,
        CloudPostFxShared shared, Layer drawn, float seconds,
        in CameraData camera, Vector3 cameraEye, Vector3 sunWorld,
        float brightness, float exposure, CloudFadeSettings fade, Vector3 skyColor, float resolutionScale, uint scatterTexture)
    {
        var layer = drawn.Params;
        float skyHeightAboveCamera = MathF.Max(1f, layer.SkyHeight - cameraEye.Y);

        // The game renders under its own exposure and this buffer is multiplied by the renderer's later, so it is divided back out.
        float skyColorGain = brightness / MathF.Max(1e-4f, exposure);
        var common = CloudBlocks.BuildCommon(palette, drawn.Colours, shared, layer, sunWorld, seconds, skyHeightAboveCamera, skyColorGain);
        ReportOnce(common);

        _composite!.Begin((int)(targets.Final.Width * resolutionScale), (int)(targets.Final.Height * resolutionScale));
        _gl.Disable(EnableCap.DepthTest);
        // Stands in for the game's disabled far clip.
        _gl.Enable(EnableCap.DepthClamp);
        // No blending into the cleared buffer: the game's alpha factors would zero the coverage against a transparent destination.
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.UseProgram(_program);
        resources.Bind(common);
        // The distance fade is the renderer's own; its extents are the dome's half-sizes.
        resources.Bind(CloudBlocks.BuildFade(fade.StartDistance, fade.Ramp, fade.Exponential, fade.Strength,
            new Vector3(layer.SkyScale, skyHeightAboveCamera, layer.SkyScale), skyColor));
        resources.Bind(CloudBlocks.BuildView(camera.View, WidenFarPlane(camera, layer.SkyScale * 4f), cameraEye, layer, skyHeightAboveCamera, DomeScale));
        BindTextures(layer, scatterTexture);

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);
        _gl.Disable(EnableCap.DepthClamp);

        // The depth test stays off, as BackgroundPass leaves it.
        _composite.Blend(targets, resources);
    }

    void BindTextures(CloudPostFxLayer layer, uint scatterTexture)
    {
        BindTexture(0, _masks![layer.BaseTextureNo]);
        BindTexture(1, _masks[layer.BaseTextureNoBlend]);
        BindTexture(2, _masks[layer.NoiseTextureNo]);
        BindTexture(3, _masks[layer.NoiseTextureNoBlend]);
        // The atmosphere's scattered light, which the fragment shader adds to the cloud colour: the sky bake's table when available.
        BindTexture(4, scatterTexture != 0 ? scatterTexture : _masks.ScatterPlaceholder);
    }

    void BindTexture(int unit, uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
    }

    // Once per session the built block is checked against the game's capture, and written out when WRS_CLOUD_DUMP names a file.
    void ReportOnce(Ubo common)
    {
        if (_dumped)
            return;
        _dumped = true;
        CloudCommonCapture.Report(common.Bytes.Span);
        if ((Environment.GetEnvironmentVariable("WRS_CLOUD_DUMP") ?? Environment.GetEnvironmentVariable("MARROW_CLOUD_DUMP")) is { Length: > 0 } path)
            File.WriteAllBytes(path, common.Bytes.ToArray());
    }
}
