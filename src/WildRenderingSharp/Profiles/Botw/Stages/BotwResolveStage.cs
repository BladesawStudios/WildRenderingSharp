using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>
/// Runs the game's own character shading passes over the G-buffer. Each draws one fullscreen triangle for the material ID it lights; the
/// game picks its pixels with the depth test, which is replaced here by a mask on the G-buffer's ID channel.
/// </summary>
public sealed class BotwResolveStage(FrameServices services) : IFrameStage, IDisposable
{
    const int IdUnit = 20, LightAnalyzedUnit = 21;

    sealed record Pass(string Name, uint Program, uint Material, float Id);

    static readonly string[] CharacterPasses =
        ["chara_nonmetal", "chara_nonmetal_direct", "chara_metal", "chara_grossy", "chara_hair", "chara_skin", "chara_eye"];

    readonly LinearDepthPass _linearDepth = new(services.Gl);
    readonly uint _flip = GLProgramBuilder.Build(services.Gl, FullscreenShaders.Vertex450, GlslFiles.Load("Botw/Flip.frag"), "botw_flip");
    readonly uint _black = Texture1x1(services.Gl, Vector4.Zero);
    List<Pass>? _passes;
    uint _lightAnalyzed;
    (Vector3 Sky, Vector3 Ground) _analyzed;

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;
        var targets = frame.Targets;
        var resources = services.Resources;
        var environment = frame.Environment as BotwEnvironment;
        _passes ??= LoadPasses();

        _linearDepth.Run(resources, targets, frame.Camera.NearPlane, frame.Camera.FarPlane);
        Clear(targets, targets.PreShadow, new Vector4(1, 1, 1, 1));
        targets.BindColorTargetLayer(targets.LightPrePassArray, 0);
        gl.ClearColor(0, 0, 0, 0);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        EnsureLightAnalyzed(frame.HemiSky, frame.HemiGround);

        Clear(targets, targets.ResolvePass, new Vector4(environment?.Background ?? default, 1f));
        targets.BindColorTarget(targets.ResolvePass);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        resources.BindEnvironment();
        resources.BindUbo(FrameUniformKeys.SceneMaterial, BotwBindings.SceneMaterial);

        ClipOrigin.Game(gl, true);
        foreach (var pass in _passes)
        {
            BindInputs(targets);
            resources.BindMaterial(pass.Material);
            gl.UseProgram(pass.Program);
            gl.SetInt(pass.Program, "uIdTex", IdUnit);
            gl.SetFloat(pass.Program, "uId", pass.Id);
            resources.DrawFullscreenTriangle();
        }
        ClipOrigin.Game(gl, false);
        GLDiagnostics.CheckPass(gl, "BotW deferred shading");

        targets.BindColorTarget(targets.Final);
        gl.UseProgram(_flip);
        gl.BindTextureUniform(_flip, "t", 0, targets.ResolvePass.Handle);
        resources.DrawFullscreenTriangle();
        gl.ActiveTexture(TextureUnit.Texture0);
        GLDiagnostics.CheckPass(gl, "BotW resolve");
    }

    void Clear(RenderTargets targets, GpuTexture target, Vector4 color)
    {
        targets.BindColorTarget(target);
        services.Gl.ClearColor(color.X, color.Y, color.Z, color.W);
        services.Gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    void BindInputs(RenderTargets targets)
    {
        BindAt(0, targets.GBuffer[1].Handle);
        BindAt(1, targets.GBuffer[3].Handle);
        BindAt(IdUnit, targets.GBuffer[0].Handle);
        BindAt(3, targets.LinearDepth.Handle);
        BindAt(5, targets.PreShadow.Handle);
        BindAt(7, _black);
        BindAt(8, targets.GBufferDepth.Handle);
        BindAt(13, targets.LightPrePassArray.Handle, TextureTarget.Texture2DArray);
        BindAt(14, targets.LinearDepthHalf.Handle);
        BindAt(LightAnalyzedUnit, _lightAnalyzed);
    }

    void BindAt(int unit, uint handle, TextureTarget target = TextureTarget.Texture2D)
    {
        services.Gl.ActiveTexture(TextureUnit.Texture0 + unit);
        services.Gl.BindTexture(target, handle);
    }

    List<Pass> LoadPasses()
    {
        var gl = services.Gl;
        var passes = new List<Pass>();
        foreach (string name in CharacterPasses)
        {
            string? file = Directory.EnumerateFiles(services.Directories.Decompiled, $"deferred_{name}_prog*_extracted.frag").Order().FirstOrDefault();
            string materialPath = Path.Combine(services.Directories.DeferredMaterials, $"{name}.gsys_material.bin");
            if (file is null || !File.Exists(materialPath))
                continue;

            byte[] material = File.ReadAllBytes(materialPath);
            float id = BitConverter.ToSingle(material, MaterialIdOffset);
            uint program = services.Programs.Load(Path.GetFileNameWithoutExtension(file), patchVertex: MoveLightAnalyzedSampler, patchFragment: MaskByMaterialId);
            passes.Add(new Pass(name, program, GLBuffer.CreatePaddedUniformBuffer(gl, material), id));
        }
        return passes;
    }

    // The pass's vertex shader reads the compare ID from material slot 26, .z, and turns it into the depth the pass draws at.
    const int MaterialIdOffset = 26 * 16 + 8;

    static string MoveLightAnalyzedSampler(string vertex) =>
        vertex.Replace("layout (binding = 0) uniform sampler2D cGSys23_LightAnalyzedData;", $"layout (binding = {LightAnalyzedUnit}) uniform sampler2D cGSys23_LightAnalyzedData;");

    static string MaskByMaterialId(string fragment) =>
        fragment.Replace("void main()\n{",
            "uniform sampler2D uIdTex;\nuniform float uId;\nvoid main()\n{\n" +
            "    if (abs(texture(uIdTex, gl_FragCoord.xy / vec2(textureSize(uIdTex, 0))).x * 255.0 - uId) > 0.5) discard;\n");

    // The vertex shader blends two samples of this strip, at 9/24 and 11/24, into the ambient colour it hands the fragment stage by screen height.
    unsafe void EnsureLightAnalyzed(Vector3 sky, Vector3 ground)
    {
        if (_lightAnalyzed != 0 && _analyzed == (sky, ground))
            return;
        _analyzed = (sky, ground);
        var gl = services.Gl;
        if (_lightAnalyzed == 0)
            _lightAnalyzed = gl.GenTexture();

        const int Width = 24;
        var texels = new float[Width * 4];
        for (int i = 0; i < Width; i++)
        {
            Vector3 color = i <= 9 ? sky : ground;
            texels[i * 4] = color.X;
            texels[i * 4 + 1] = i < 2 ? 1f : color.Y;
            texels[i * 4 + 2] = color.Z;
            texels[i * 4 + 3] = 1f;
        }
        gl.BindTexture(TextureTarget.Texture2D, _lightAnalyzed);
        fixed (float* data = texels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, Width, 1, 0, PixelFormat.Rgba, PixelType.Float, data);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    static unsafe uint Texture1x1(GL gl, Vector4 color)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        float[] data = [color.X, color.Y, color.Z, color.W];
        fixed (float* p = data)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, p);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        return handle;
    }

    public void Dispose()
    {
        var gl = services.Gl;
        _linearDepth.Dispose();
        gl.DeleteProgram(_flip);
        gl.DeleteTexture(_black);
        if (_lightAnalyzed != 0)
            gl.DeleteTexture(_lightAnalyzed);
        foreach (var pass in _passes ?? [])
            gl.DeleteBuffer(pass.Material);
    }
}
