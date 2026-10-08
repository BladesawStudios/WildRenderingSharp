using WildRenderingSharp.Graphics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// Runs each distinct deferred resolve program the loaded models need: an unmodified game shader per pass over a fullscreen quad it
/// synthesises from <c>gl_VertexID</c>, composited into the running result only where the pass-ID mask names it.
/// </summary>
public sealed class DeferredResolvePass : IDisposable
{
    readonly GL _gl;
    readonly uint _composeProgram;
    readonly uint _texPreFog, _texVolumeMask;

    public const string FieldFallbackPass = "chara_nonmetal";

    // The field passes that run their own program. field_water reads the same screen-space inputs as the chara passes plus
    // cTex_CubeEnvMap; water was black because of its G-buffer half (see SceneColorShapePass), and lighting it as an opaque
    // nonmetal surface was never right.
    static readonly HashSet<string> RealFieldPasses = ["field_water"];

    const int CubeEnvMapUnit = 9;

    readonly uint _cubeEnvMap;

    static readonly string ComposeFragmentSource = GlslFiles.Load("Totk/Deferred/DeferredResolve/Compose.frag");

    public DeferredResolvePass(GL gl)
    {
        _gl = gl;
        _composeProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ComposeFragmentSource, "mask_compose");
        _texPreFog = CreateConstTexture2D(0f, 0f, 0f, 0f);
        _texVolumeMask = CreateConstTexture2D(0f, 0f, 0f, 0f);
        _cubeEnvMap = _gl.GenTexture();
        SetEnvironmentColor(new System.Numerics.Vector3(0.5f));
    }

    public unsafe void SetEnvironmentColor(System.Numerics.Vector3 color)
    {
        _gl.BindTexture(TextureTarget.TextureCubeMap, _cubeEnvMap);
        float* texel = stackalloc float[] { color.X, color.Y, color.Z, 1f };
        for (int face = 0; face < 6; face++)
            _gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, InternalFormat.Rgba16f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, texel);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, 0);
        _gl.BindTexture(TextureTarget.TextureCubeMap, 0);
    }

    public static List<ResolvedDeferredPass> ResolveDeferredPasses(
        GL gl, ShaderProgramCache programs, string decompiledDir, string deferredMaterialsDir, IEnumerable<string> passNames)
    {
        var resolved = new List<ResolvedDeferredPass>();
        int passIndex = -1;
        foreach (string rawName in passNames)
        {
            passIndex++;
            string name = rawName;
            if (name.StartsWith("field_", StringComparison.Ordinal) && !RealFieldPasses.Contains(name))
            {
                Console.WriteLine($"  [approx] {name} needs preshading-only inputs; resolving through {FieldFallbackPass}");
                name = FieldFallbackPass;
            }

            string? hit = name.Length == 0 ? null : Directory.EnumerateFiles(decompiledDir, $"deferred_{name}_prog*_extracted.frag")
                .OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (hit is null && name != FieldFallbackPass)
            {
                // A pass with no program of its own (an o_material_behave nothing maps, exported as an empty name) is lit as the common case rather than left unlit.
                Console.WriteLine($"  [approx] no extracted deferred shader for pass '{name}'; resolving through {FieldFallbackPass}");
                name = FieldFallbackPass;
                hit = Directory.EnumerateFiles(decompiledDir, $"deferred_{name}_prog*_extracted.frag")
                    .OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            }
            if (hit is null)
            {
                Console.WriteLine($"  [skip] no extracted deferred shader for pass '{name}'");
                continue;
            }
            string baseName = Path.GetFileNameWithoutExtension(hit);
            uint program = programs.Load(baseName);

            string matPath = Path.Combine(deferredMaterialsDir, $"{name}.gsys_material.bin");
            byte[] matBytes = File.Exists(matPath) ? File.ReadAllBytes(matPath) : [];
            if (matBytes.Length == 0)
                Console.WriteLine($"  [warn] no deferred gsys_material for pass '{name}' at '{matPath}' - resolving with an all-zero Mat block");
            uint matBuffer = GLBuffer.CreatePaddedUniformBuffer(gl, matBytes);

            resolved.Add(new ResolvedDeferredPass(rawName, program, matBuffer, passIndex));
        }
        return resolved;
    }

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ResolvedDeferredPass> passes,
        float emissionScale, float sceneGain, float exposure, Func<string, bool>? include = null, int claimEmptyPass = -1)
    {
        // Final already holds the background (painted by the caller), and the compose step only touches pixels its pass-ID mask claims, so any uncovered pixel keeps it. Do not clear here.
        targets.BindColorTarget(targets.Final);
        _gl.Disable(EnableCap.DepthTest);

        for (int i = 0; i < passes.Count; i++)
        {
            var pass = passes[i];
            if (include is not null && !include(pass.Name))
                continue;

            // Rebound every pass: the mask-compose step claims units 0/1 (G-buffer albedo and normal), which would otherwise be the previous pass's output.
            BindResolveInputs(targets);
            resources.BindMaterial(pass.MaterialBuffer);

            targets.BindColorTarget(targets.ResolvePass);
            _gl.ClearColor(0, 0, 0, 1);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            _gl.UseProgram(pass.Program);
            resources.DrawFullscreenQuadStrip();

            targets.BindColorTarget(targets.Final);
            _gl.UseProgram(_composeProgram);
            _gl.BindTextureUniform(_composeProgram, "t", 0, targets.ResolvePass.Handle);
            _gl.BindTextureUniform(_composeProgram, "tex_id", 1, targets.PassId.Handle);
            _gl.BindTextureUniform(_composeProgram, "tex_emis", 20, targets.GBuffer[5].Handle);
            _gl.BindTextureUniform(_composeProgram, "tex_alb", 21, targets.GBuffer[1].Handle);
            _gl.SetFloat(_composeProgram, "uEmission", emissionScale);
            _gl.SetFloat(_composeProgram, "uEmissionExposureRcp", exposure > 1e-4f ? 1f / exposure : 1f);
            _gl.SetFloat(_composeProgram, "uSceneGain", sceneGain);
            _gl.SetInt(_composeProgram, "uGFlip", 1);
            _gl.SetFloat(_composeProgram, "uId", (pass.PassIndex + 1) / 255f);
            _gl.SetInt(_composeProgram, "uAll", 0);
            _gl.SetInt(_composeProgram, "uClaimEmpty", pass.PassIndex == claimEmptyPass ? 1 : 0);
            _gl.BindTextureUniform(_composeProgram, "tex_gdepth", 22, targets.GBufferDepth.Handle);
            resources.DrawFullscreenTriangle();
        }
    }

    void BindResolveInputs(RenderTargets targets)
    {
        // Units and semantics are fixed by the game's compiled resolve shaders; some inputs are authentic, others neutral stand-ins.
        BindAt(0, targets.GBuffer[1].Handle);       // cTex_GBuffAlbedo
        BindAt(1, targets.GBuffer[3].Handle);       // cTex_GBuffNormal
        BindAt(4, targets.LinearDepth.Handle);      // cTex_NormalizedLinearDepth
        BindAt(5, targets.LinearDepthHalf.Handle);  // cTex_HalfNormalizedLinearDepth
        BindAt(11, _texVolumeMask);                 // cTex_VolumeMask (neutral - inert; Env[81] carries the palette's real tint)
        BindAt(16, targets.PreShadow.Handle);        // cTex_PreShadow (WildRenderingSharp-synthesised)
        BindAt(17, _texPreFog);                      // cTex_PreFog (neutral - no aerial perspective)
        BindAt(18, targets.PreMisc.Handle);           // cTex_PreMisc (WildRenderingSharp-synthesised)
        BindAt(28, targets.LightPrePassArray.Handle, TextureTarget.Texture2DArray); // cTex_DeferredLightPrePass - see LightPrePass
        BindAt(CubeEnvMapUnit, _cubeEnvMap, TextureTarget.TextureCubeMap); // cTex_CubeEnvMap (field_water) - see SetEnvironmentColor
    }

    void BindAt(int unit, uint handle, TextureTarget target = TextureTarget.Texture2D)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(target, handle);
    }

    unsafe uint CreateConstTexture2D(float r, float g, float b, float a)
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        float[] data = [r, g, b, a];
        fixed (float* ptr = data)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, ptr);
        SetRepeatLinear(TextureTarget.Texture2D, handle);
        return handle;
    }

    void SetRepeatLinear(TextureTarget target, uint handle)
    {
        _gl.BindTexture(target, handle);
        _gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_composeProgram);
        _gl.DeleteTexture(_texPreFog);
        _gl.DeleteTexture(_texVolumeMask);
        _gl.DeleteTexture(_cubeEnvMap);
    }
}
