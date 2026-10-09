using WildRenderingSharp.Assets.Materials;
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

    const int CubeEnvMapUnit = 9;

    readonly uint _cubeEnvMap;

    // The storage buffer (vp_s0) field_hybrid's vertex stage reads to learn which screen tiles hold surfaces: instance i is kept only if the
    // dword at byte 0x41C0 + 16 i + 12 is 1. The game fills it with a compute pass; here the grid is one tile (ContextUbo.WithTileGrid).
    uint TileFlagsBuffer
    {
        get
        {
            if (_tileFlags == 0)
            {
                const int FlagByte = 0x41C0 + 12;
                byte[] data = new byte[FlagByte + 16];
                BitConverter.TryWriteBytes(data.AsSpan(FlagByte), 1u);
                _tileFlags = _gl.GenBuffer();
                _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _tileFlags);
                _gl.BufferData<byte>(BufferTargetARB.ShaderStorageBuffer, data, BufferUsageARB.StaticDraw);
                _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
            }
            return _tileFlags;
        }
    }

    uint _tileFlags;

    // The pass whose own output is kept for the debug views, or -1.
    public int DebugPass { get; set; } = -1;

    string? _tracePass;
    Func<ResolvedDeferredPass, ResolveTrace>? _traceStart;

    internal void Trace(string pass, Func<ResolvedDeferredPass, ResolveTrace> start)
    {
        _tracePass = pass;
        _traceStart = start;
    }

    static readonly string ComposeFragmentSource = GlslFiles.Load("Totk/Deferred/DeferredResolve/Compose.frag");

    public DeferredResolvePass(GL gl)
    {
        _gl = gl;
        _composeProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ComposeFragmentSource, "mask_compose");
        _texPreFog = CreateConstTexture2D(0f, 0f, 0f, 0f);
        _texVolumeMask = CreateConstTexture2D(0f, 0f, 0f, 0f);
        _cubeEnvMap = _gl.GenTexture();
        SetEnvironment(new System.Numerics.Vector3(0.5f), new System.Numerics.Vector3(0.2f));
    }

    // The cube's edge in texels at its finest level; the field programs read down to level 3, by roughness.
    const int CubeSize = 16;

    /// <summary>Fills <c>cTex_CubeEnvMap</c> with sky above the horizon and ground below (the game reflects a probe of the scene). Up is +Y, the game's.</summary>
    public unsafe void SetEnvironment(System.Numerics.Vector3 sky, System.Numerics.Vector3 ground)
    {
        if (_cubeSky == sky && _cubeGround == ground)
            return;
        _cubeSky = sky;
        _cubeGround = ground;

        int levels = 1;
        for (int n = CubeSize; n > 1; n >>= 1) levels++;
        _gl.BindTexture(TextureTarget.TextureCubeMap, _cubeEnvMap);
        for (int level = 0; level < levels; level++)
        {
            int n = Math.Max(1, CubeSize >> level);
            float[] texels = new float[n * n * 4];
            for (int face = 0; face < 6; face++)
            {
                for (int t = 0; t < n; t++)
                {
                    for (int sIdx = 0; sIdx < n; sIdx++)
                    {
                        float u = 2f * (sIdx + 0.5f) / n - 1f, v = 2f * (t + 0.5f) / n - 1f;
                        var d = face switch
                        {
                            0 => new System.Numerics.Vector3(1f, -v, -u),
                            1 => new System.Numerics.Vector3(-1f, -v, u),
                            2 => new System.Numerics.Vector3(u, 1f, v),
                            3 => new System.Numerics.Vector3(u, -1f, -v),
                            4 => new System.Numerics.Vector3(u, -v, 1f),
                            _ => new System.Numerics.Vector3(-u, -v, -1f),
                        };
                        d = System.Numerics.Vector3.Normalize(d);
                        float up = Math.Clamp((d.Y + 0.15f) / 0.3f, 0f, 1f);
                        up = up * up * (3f - 2f * up);
                        var c = System.Numerics.Vector3.Lerp(ground, sky, up);
                        int at = (t * n + sIdx) * 4;
                        texels[at] = c.X; texels[at + 1] = c.Y; texels[at + 2] = c.Z; texels[at + 3] = 1f;
                    }
                }
                fixed (float* ptr = texels)
                    _gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, level, InternalFormat.Rgba16f, (uint)n, (uint)n, 0, PixelFormat.Rgba, PixelType.Float, ptr);
            }
        }
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, levels - 1);
        _gl.BindTexture(TextureTarget.TextureCubeMap, 0);
    }

    System.Numerics.Vector3 _cubeSky = new(float.NaN), _cubeGround = new(float.NaN);

    public static List<ResolvedDeferredPass> ResolveDeferredPasses(
        GL gl, ShaderProgramCache programs, string decompiledDir, string deferredMaterialsDir, IEnumerable<string> passNames)
    {
        var resolved = new List<ResolvedDeferredPass>();
        int passIndex = -1;
        foreach (string rawName in passNames)
        {
            passIndex++;
            string name = rawName;

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
            var material = new MaterialBlock(gl, matBytes);

            resolved.Add(new ResolvedDeferredPass(rawName, program, material, passIndex,
                FieldLights: name.StartsWith("field_", StringComparison.Ordinal), Tiled: name is "field_hybrid" or "field_hybrid_all_shadow", Source: baseName));
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
            BindAt(28, (pass.FieldLights ? targets.FieldLightPrePassArray : targets.LightPrePassArray).Handle, TextureTarget.Texture2DArray);
            resources.BindMaterial(pass.Material);

            targets.BindColorTarget(targets.ResolvePass);
            _gl.ClearColor(0, 0, 0, 1);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            ResolveTrace? trace = null;
            if (_tracePass == pass.Name && _traceStart is not null && pass.Source.Length > 0)
            {
                trace = _traceStart(pass);
                _tracePass = null;
            }
            _gl.UseProgram(trace?.Program ?? pass.Program);
            trace?.Bind();
            if (pass.Tiled)
            {
                resources.BindCamera(TotkUniformKeys.FieldCamera);
                _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, TileFlagsBuffer);
            }
            resources.DrawFullscreenQuadStrip();
            if (pass.Tiled)
                resources.BindCamera(FrameUniformKeys.SceneCamera);
            if (trace is not null)
            {
                trace.Finish();
                trace.Dispose();
            }
            if (pass.PassIndex == DebugPass)
                targets.SnapshotResolve();

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
        BindAt(2, targets.GBuffer[0].Handle);       // cTex_GBuffMaterialID, attachment 0 as the G-buffer programs write it
        BindAt(4, targets.LinearDepth.Handle);      // cTex_NormalizedLinearDepth
        BindAt(5, targets.LinearDepthHalf.Handle);  // cTex_HalfNormalizedLinearDepth
        BindAt(11, _texVolumeMask);                 // cTex_VolumeMask (neutral - inert; Env[81] carries the palette's real tint)
        BindAt(16, targets.PreShadow.Handle);        // cTex_PreShadow (WildRenderingSharp-synthesised)
        BindAt(17, _texPreFog);                      // cTex_PreFog (neutral - no aerial perspective)
        BindAt(18, targets.PreMisc.Handle);           // cTex_PreMisc (WildRenderingSharp-synthesised)
        BindAt(28, targets.LightPrePassArray.Handle, TextureTarget.Texture2DArray); // cTex_DeferredLightPrePass - see LightPrePass
        BindAt(CubeEnvMapUnit, _cubeEnvMap, TextureTarget.TextureCubeMap); // cTex_CubeEnvMap (field_water) - see SetEnvironment
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
        if (_tileFlags != 0)
            _gl.DeleteBuffer(_tileFlags);
    }
}
