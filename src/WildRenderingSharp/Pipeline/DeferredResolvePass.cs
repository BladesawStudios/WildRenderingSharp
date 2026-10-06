using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>One deferred resolve pass, pre-resolved to its real compiled program and its own <c>SystemModel.DeferredMain</c> material bytes - see <see cref="DeferredResolvePass.ResolveDeferredPasses"/>.</summary>
/// <param name="PassIndex">
/// The pass's position in the list it was resolved from - the pass-ID mask's numbering, which is
/// how its pixels are found. Not its position among the resolved passes: a pass with no program is
/// skipped, and every pass after it used to be matched to the next one's pixels.
/// </param>
public sealed record ResolvedDeferredPass(string Name, uint Program, uint MaterialUboBuffer, int PassIndex);

/// <summary>
/// Runs each distinct deferred resolve program the loaded model actually needs - a real,
/// unmodified game shader per pass, over a fullscreen quad the resolve vertex shader synthesises
/// itself from <c>gl_VertexID</c> - and composites it into the running result only where the
/// pass-ID mask names it. Mirrors <c>load_deferred_prog</c>/<c>resolve_passes</c>/the resolve loop
/// in <c>render_scene</c>/<c>MASK_COMPOSE_SRC</c>.
/// </summary>
public sealed class DeferredResolvePass : IDisposable
{
    readonly GL _gl;
    readonly uint _composeProgram;
    readonly uint _texPreFog, _texVolumeMask;

    /// <summary><c>o_material_behave = 2</c> spans the whole field_* family, none of which can run correctly without the preshading passes/undecoded Env tail - substituted with a logged fallback rather than rendering black. See <c>FIELD_FALLBACK</c>.</summary>
    public const string FieldFallbackPass = "chara_nonmetal";

    /// <summary>
    /// The field passes that run their own program after all. <c>field_water</c> reads the same
    /// screen-space inputs the chara passes do, plus <c>cTex_CubeEnvMap</c> for its reflection;
    /// what made water black was its G-buffer half (see <see cref="SceneColorShapePass"/>), not
    /// this one, and lighting its scatter colour as an opaque nonmetal surface was never right.
    /// </summary>
    static readonly HashSet<string> RealFieldPasses = ["field_water"];

    /// <summary><c>cTex_CubeEnvMap</c>'s unit in the resolve programs that read it.</summary>
    const int CubeEnvMapUnit = 9;

    readonly uint _cubeEnvMap;

    const string QuadVertexSource = """
        #version 450 core
        out vec2 vUV;
        void main() {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    const string ComposeFragmentSource = """
        #version 450 core
        uniform sampler2D t;         // one pass's fullscreen resolve
        uniform sampler2D tex_id;    // the pass-ID buffer
        uniform sampler2D tex_emis;  // cTex_GBuffEmission (G-buffer attachment 5)
        uniform sampler2D tex_alb;   // cTex_GBuffAlbedo   (G-buffer attachment 1)
        uniform sampler2D tex_gdepth; // the G-buffer's depth
        uniform int uClaimEmpty;     // this pass also lights geometry no actor stamped (the terrain)
        uniform float uId;
        uniform float uEmission;
        uniform float uEmissionExposureRcp; // see Run's remarks - keeps emission exposure-invariant
        uniform float uSceneGain;
        uniform int uAll;
        uniform int uGFlip;
        in vec2 vUV;
        out vec4 fragColor;
        void main() {
            float id_val = texture(tex_id, vUV).r;
            vec2 g = uGFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
            if (id_val <= 0.001) {
                if (uClaimEmpty == 0 || texture(tex_gdepth, g).r >= 1.0) discard;
            }
            else if (uAll == 0 && abs(id_val - uId) > 0.6 / 255.0) discard;

            float enable = float(int(trunc(texture(tex_alb, g).a * 255.0)) & 1);

            vec3 lit_col = texture(t, vUV).rgb;
            if (isnan(lit_col.r) || isnan(lit_col.g) || isnan(lit_col.b) || isinf(lit_col.r) || isinf(lit_col.g) || isinf(lit_col.b))
                lit_col = vec3(0.0);
            // Clamped at zero because emission is radiance ADDED to the scene - there is no such
            // thing as a surface that emits negative light, so this can never alter a correct
            // material. It guards against a decompilation artifact: Ryujinx renders negation as
            // "0.0 - x", and in some shaders that has been mis-associated into a stray "v * 0.0"
            // term, leaving an emission expression that is negative for EVERY input. Enemy_MiasmaTentacle
            // and Npc_Ganondorf_Mummy both write their skin emission through one of those
            // (material_prog10338, whose temp_40 is provably <= -0.333 whatever the uniforms say),
            // and without this their bodies are actively darkened to black by their own emission.
            vec3 emission = max(texture(tex_emis, g).rgb, vec3(0.0)) * enable * uEmission * uEmissionExposureRcp;
            fragColor = vec4(lit_col * uSceneGain + emission, 1.0);
        }
        """;

    public DeferredResolvePass(GL gl)
    {
        _gl = gl;
        _composeProgram = GLProgramBuilder.Build(gl, QuadVertexSource, ComposeFragmentSource, "mask_compose");
        _texPreFog = CreateConstTexture2D(0f, 0f, 0f, 0f);
        _texVolumeMask = CreateConstTexture2D(0f, 0f, 0f, 0f);
        _cubeEnvMap = _gl.GenTexture();
        SetEnvironmentColor(new System.Numerics.Vector3(0.5f));
    }

    /// <summary>
    /// Fills <c>cTex_CubeEnvMap</c>. WildRenderingSharp renders no environment cube, so every face is
    /// one colour - the palette's sky colour, which is what an open-air reflection mostly shows.
    /// </summary>
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

    /// <summary>
    /// Resolves each distinct deferred-pass name to its real compiled program (globbed by name,
    /// not a hardcoded index - a model that resolves through a different pass just works) and its
    /// own material bytes, once per loaded model (not per frame - these never change while a
    /// model stays loaded). A <c>field_*</c> pass is substituted with <see cref="FieldFallbackPass"/>.
    /// </summary>
    /// <param name="deferredMaterialsDir">
    /// Where <c>&lt;pass&gt;.gsys_material.bin</c> lives - a directory SHARED across every loaded
    /// model (see <c>ModelPreparer.DefaultDeferredMaterialsDirectory</c>/
    /// <c>BuildMaterialUbo.RunSystemDeferred</c>), not the per-model data directory: these bytes
    /// come from the one shared system shader archive + <c>SystemModel.DeferredMain</c> model,
    /// identical for every creature. A missing file here means this step was never run (an older
    /// cache) - the pass still resolves and draws, just with an all-zero "Mat" block, which is a
    /// real, silent bug class in itself (see this file's own history) rather than something to
    /// paper over further here.
    /// </param>
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
                // A pass with no program of its own (an o_material_behave value nothing maps, which
                // exports as an empty name) is lit as the common case rather than left unlit.
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

    /// <param name="emissionScale">The viewer's Emission Scale control. 1.0 now means "the magnitude the material authored", because of <paramref name="exposure"/> below.</param>
    /// <param name="exposure">
    /// The exposure <see cref="TonemapPass.RunExposureAndCompress"/> will apply to this whole
    /// buffer afterwards, divided back out of the emission term here so emission lands at its
    /// authored magnitude regardless of it.
    ///
    /// WHY: WildRenderingSharp's exposure is not the game's. A real ResEnvPalette authors
    /// <c>Exposure: 1.0</c>; WildRenderingSharp's default is 10, a calibration standing in for the lighting it
    /// does not compute (no preshading passes, no cubemap IBL, no light pre-pass). The emission a
    /// material writes into G-buffer attachment 5 is authored in the GAME's units, so multiplying
    /// it by that 10x lit-path correction blew every emissive surface past the highlight-compression
    /// ceiling: Enemy_Giant's eye (emission = emissive texture * (1 + 7 * albedo), from its own
    /// gsys_material) rendered as flat white, and Enemy_Dragon_Darkness's claws rendered their
    /// authored magenta (Mt_Nail's emission colour really is (0.78, 0.0, 0.08)) far too hot.
    /// Dividing it back out here is the same separation <c>LightingContext.SceneGain</c> already
    /// documents for the lit path, applied to the axis that actually needed it.
    /// </param>
    /// <param name="claimEmptyPass">The pass that also lights geometry the pass-ID mask left unstamped - a host's terrain - or -1.</param>
    /// <param name="include">Which passes to run, by name - all of them when null. A skipped pass keeps its pass-ID, so the mask still names the rest correctly.</param>
    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ResolvedDeferredPass> passes,
        float emissionScale, float sceneGain, float exposure, Func<string, bool>? include = null, int claimEmptyPass = -1)
    {
        // Final's background content (colour/transparent/sky - see BackgroundPass) is already
        // painted in by the caller, BEFORE this runs - any pixel no resolved pass's mask covers
        // below keeps that content all the way to the final displayed image, since the compose
        // step's own discard only ever touches a pixel its pass-ID mask claims. Do NOT clear here.
        targets.BindColorTarget(targets.Final);
        _gl.Disable(EnableCap.DepthTest);

        for (int i = 0; i < passes.Count; i++)
        {
            var pass = passes[i];
            if (include is not null && !include(pass.Name))
                continue;

            // Rebound every pass: the mask-compose step below claims units 0/1 (G-buffer albedo
            // and normal), which would otherwise be pass i-1's own output on the second and later
            // passes of a multi-pass model.
            BindResolveInputs(targets);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, 8, pass.MaterialUboBuffer);

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
        // Units and semantics fixed by the game's own compiled resolve shaders - see the module
        // remarks on which are genuinely authentic vs. WildRenderingSharp-synthesised neutrals.
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
