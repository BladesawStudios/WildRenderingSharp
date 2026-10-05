using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Draws blended materials forward, after the deferred resolve - a blended surface can't go
/// through the deferred G-buffer at all (there's nothing to blend against yet), so the engine
/// draws it with <c>gsys_assign_material</c> once the scene behind it is resolved.
///
/// The SAME program is ALSO how the game composites opaque status-effect overlays (miasma,
/// ice/chemical status, camouflage mottling, damage flash, procedural dissolve) on an otherwise-
/// opaque surface - confirmed by reading the real decompiled shader for materials with no blend at
/// all that still read exactly those <c>gsys_material</c> fields and nothing else. Running it
/// unconditionally for EVERY opaque/masked shape that resolves one is wrong two ways, both
/// confirmed: it visibly breaks otherwise-correct models (the forward program is a fully
/// independent relight with no equivalent of the toon/ink outline that's baked into the real
/// deferred shaders, so it silently erases it), and separately produces a full-body cyan
/// oversaturation on Enemy_MiasmaTentacle/a Ganondorf model whose real cause is still open (see
/// <c>docs/forward_pass_ubo_map.md</c>). Traced the real engine's own per-object gate as far as
/// static analysis can take it (same doc, "Investigation log" section): it's driven by dynamic
/// gameplay/status state WildRenderingSharp has no equivalent system for, not by anything in the static
/// material/shader data. So this is a per-MATERIAL opt-in (<see cref="LoadedShape.ForceForward"/>,
/// set via the Material Inspector) rather than a global toggle or an auto-detected condition - the
/// user manually enables it on the specific materials that are ALWAYS meant to run this pass (e.g.
/// a permanently-corrupted enemy), leaving it off for everything else.
///
/// The G-buffer renders through a Y-flipped projection (NVN's upper-left-origin convention) while
/// the resolve writes the true GL orientation, so the resolved scene is flipped into G-buffer
/// space, drawn into, and flipped back - that's what lets the forward geometry depth-test against
/// the G-buffer depth it was rasterised alongside. <c>cTex_ColorBuffer</c> must be a COPY of the
/// scene, never the live render target (sampling what you're drawing into is a GL feedback loop).
/// Mirrors the forward half of <c>render_scene</c>/<c>FLIP_BLIT_SRC</c>.
/// </summary>
public sealed class ForwardPass : IDisposable
{
    readonly GL _gl;
    readonly uint _flipProgram, _floorProgram;
    readonly uint _texWhite, _texVolumeMask, _texNoise3D, _texArrayWhite, _texShadowCascadeArray;
    static readonly int[] WhiteNeutralUnits = [7, 11, 13, 14, 15, 31];

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

    const string FlipFragmentSource = """
        #version 450 core
        uniform sampler2D t;
        uniform int uFlip;
        in vec2 vUV;
        out vec4 fragColor;
        void main() {
            vec4 c = texture(t, uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV);
            // A generic safety net for the two plain, pre-forward-draw copies this is used for
            // (Scene<-Final, Behind<-Scene) - neither has any forward-pass content in it yet, so
            // this is just insurance against an already-broken upstream value, not a fix for
            // anything specific. See FloorFragmentSource/FlipIntoWithFloor for how the actual
            // deferred+forward combination step (which DOES need real floor logic) is handled.
            fragColor = vec4(max(c.rgb, -1.0), c.a);
        }
        """;

    // Used ONLY for the final Scene->Final step, once forward geometry has been additively drawn
    // into Scene. Floors the combined result at `tFloor` (targets.Behind - a snapshot of the
    // deferred-only content captured BEFORE any forward drawing, see Run's remarks) instead of an
    // arbitrary constant. WHY: confirmed, on Enemy_MiasmaTentacle's Mt_Skin, both algebraically and
    // via the live numeric probe, that this program's own "base colour" term is negative for every
    // possible input - not a wrong/missing UBO value (every real material constant feeding it was
    // checked and is correctly authored), but a genuine property of the compiled formula, matching
    // an ALREADY-DOCUMENTED decompiler artifact this exact codebase has hit before (see
    // DeferredResolvePass.cs's own ComposeFragmentSource remarks on material_prog10338's emission:
    // "Ryujinx renders negation as '0.0 - x', and in some shaders that has been mis-associated into
    // a stray 'v * 0.0' term, leaving an expression that is negative for EVERY input" - temp_102 in
    // THIS program, material_prog10336, has the exact same "<var> * 0.0" shape and the exact same
    // provably-negative-regardless-of-uniforms signature). That fix clamped the corrupted value at
    // the point WildRenderingSharp CONSUMES it (never inside the decompiled shader text itself - see
    // GlslSanitizer.cs's own explicit rule against editing shader logic). This is the same
    // philosophy applied here: an arbitrary numeric floor (tried -1.0, then 0.0 before that) can't
    // fix a channel whose delta is larger in magnitude than the deferred base's own tiny value
    // (confirmed: deferred-alone red 0.0018, forward's own red delta -0.0812 - no fixed floor
    // makes that combination positive). Flooring against the REAL deferred value instead means the
    // forward pass can only ever brighten a pixel relative to what the G-buffer already correctly
    // computed, never darken it below that - a genuine highlight still shows through fully (whole
    // channels stay unclamped upward), while this program's confirmed-corrupted negative term can
    // no longer erase real, correctly-lit colour.
    const string FloorFragmentSource = """
        #version 450 core
        uniform sampler2D t;       // Scene: deferred + forward, additively combined
        uniform sampler2D tFloor;  // Behind: deferred-only, captured before any forward drawing
        uniform int uFlip;
        in vec2 vUV;
        out vec4 fragColor;
        void main() {
            vec2 uv = uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
            vec3 combined = texture(t, uv).rgb;
            vec3 floorRgb = texture(tFloor, uv).rgb;
            fragColor = vec4(max(combined, floorRgb), texture(t, uv).a);
        }
        """;

    public unsafe ForwardPass(GL gl, string? systemTexturesDirectory = null)
    {
        _gl = gl;
        _flipProgram = GLProgramBuilder.Build(gl, QuadVertexSource, FlipFragmentSource, "flip_blit");
        _floorProgram = GLProgramBuilder.Build(gl, QuadVertexSource, FloorFragmentSource, "flip_blit_floor");

        _texWhite = CreateConstTexture2D(1, 1, 1, 1);
        _texVolumeMask = CreateConstTexture2D(0, 0, 0, 0);
        _texArrayWhite = CreateConstTexture2DArray(1, 1, 1, 1, layers: 1);

        _texNoise3D = LoadRealNoiseVolumeOrFallback(gl, systemTexturesDirectory);

        // A 1x1x1 depth-array texture with comparison enabled and depth 1.0 (LEQUAL): "nothing
        // occludes" - the neutral value for cTex_DepthShadowCascade, a sampler2DArrayShadow the
        // forward shader genuinely samples (an unbound/wrong-type sampler here reads as
        // undefined, which showed up as NaN across the whole frame in the Python bench).
        _texShadowCascadeArray = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, _texShadowCascadeArray);
        float depthOne = 1.0f;
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent32f, 1, 1, 1, 0, PixelFormat.DepthComponent, PixelType.Float, &depthOne);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
    }

    public static bool HasForwardShapes(IReadOnlyList<LoadedShape> shapes) => shapes.Any(s => s.HasForward);

    public void FlipInto(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, bool flip)
    {
        _gl.UseProgram(_flipProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_flipProgram, "t", 0, src.Handle);
        _gl.SetInt(_flipProgram, "uFlip", flip ? 1 : 0);
        resources.DrawFullscreenTriangle();
    }

    /// <summary>Like <see cref="FlipInto"/>, but floors the result at <paramref name="floorTex"/> instead of copying <paramref name="src"/> unconditionally - see <c>FloorFragmentSource</c>'s own remarks.</summary>
    public void FlipIntoWithFloor(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, GpuTexture floorTex, bool flip)
    {
        _gl.UseProgram(_floorProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_floorProgram, "t", 0, src.Handle);
        _gl.BindTextureUniform(_floorProgram, "tFloor", 1, floorTex.Handle);
        _gl.SetInt(_floorProgram, "uFlip", flip ? 1 : 0);
        resources.DrawFullscreenTriangle();
    }

    /// <summary>Draws every placed actor's blended/force-forward shapes into the same forward-resolved scene, rebinding each actor's own skinning UBOs (<see cref="ActorDrawGroup"/>) before its own shapes.</summary>
    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups)
    {
        var forwardGroups = groups
            .Select(g => (Group: g, Forward: g.Shapes.Where(s => s.HasForward && (s.Blend || s.ForceForward)).ToList()))
            .Where(x => x.Forward.Count > 0)
            .ToList();
        if (forwardGroups.Count == 0)
            return;

        _gl.Disable(EnableCap.Blend);
        FlipInto(resources, targets, targets.Scene, targets.Final, flip: true);   // into the G-buffer's orientation
        FlipInto(resources, targets, targets.Behind, targets.Scene, flip: false); // a copy, for cTex_ColorBuffer

        targets.BindColorAndDepthTarget(targets.Scene, targets.GBufferDepth);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        // The forward geometry is rasterised into targets.Scene/GBufferDepth - the G-BUFFER's
        // flipped orientation (see this class's own remarks: that's the whole reason Scene is
        // built by flipping Final into it, and the only way this draw can depth-test against
        // GBufferDepth at all). The compiled forward vertex shader reads the shared Context UBO
        // like every other TotK shader does, and whatever DeferredPipeline.RenderFrame left bound
        // there by this point is "ctx_true" (rebound right after the G-buffer pass, and never
        // switched back before here) - so without this, the shape is transformed in the WRONG,
        // unflipped space while everything around it is flipped, then the final FlipInto below
        // flips the whole buffer back ONCE MORE, net-flipping only the newly-drawn geometry and
        // leaving it a vertically mirrored duplicate of itself relative to the (correctly
        // unflipped-then-reflipped) background. Previously invisible because so few materials
        // ever drew here (blend-only); now every opaque/masked forward-resolved shape does.
        resources.BindUbo("ctx_gbuffer", 1);

        BindAt(5, targets.LinearDepthHalf.Handle);
        BindAt(30, targets.Behind.Handle);
        foreach (int unit in WhiteNeutralUnits)
            BindAt(unit, _texWhite);
        BindAt(10, _texVolumeMask);
        BindAt(9, _texNoise3D, TextureTarget.Texture3D);
        BindAt(29, _texArrayWhite, TextureTarget.Texture2DArray);
        BindAt(6, _texShadowCascadeArray, TextureTarget.Texture2DArray);

        foreach (var (group, forward) in forwardGroups)
        {
        group.BindUbos(resources);
        foreach (var sh in forward)
        {
            if (sh.Blend)
            {
                var (funcs, ops) = sh.RenderState.ResolveBlendState();
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFuncSeparate(funcs.SrcRgb, funcs.DstRgb, funcs.SrcAlpha, funcs.DstAlpha);
                _gl.BlendEquationSeparate(ops.Rgb, ops.Alpha);
            }
            else
            {
                // Opaque/masked shapes redraw here for their status-effect overlay (see this
                // class's remarks) - ADDITIVE, not a replace. Hand-traced Enemy_MiasmaTentacle's
                // Mt_Skin forward program (material_prog10336_extracted.frag) end to end and
                // confirmed, both algebraically and via the live numeric probe, that its own
                // "base colour" term is NEGATIVE for every possible input - not a missing/wrong
                // UBO value (every real material constant feeding it, including the correctly-
                // authored p_const_color2/p_const_color3, was checked and is fine), an inherent
                // property of the compiled formula itself. No real material's standalone albedo
                // is designed to be inherently negative - that only makes sense if this program's
                // output was never meant to REPLACE the pixel, but to be composited as a
                // delta/overlay on top of the already-correctly-lit deferred result (miasma/ice/
                // damage-flash tinting, by design). A plain replace (the previous behaviour)
                // discarded the correct deferred-lit pixel outright and substituted this
                // near-always-negative delta directly - clamped to black by FlipFragmentSource's
                // safety net, with only the rare pixel where the term goes briefly positive
                // surviving as a thin rim, exactly the "flat black hand with a glowing edge"
                // symptom reported. Additive blending lets a negative contribution genuinely
                // darken/tint the correct base instead of annihilating it outright.
                //
                // REVERTED the exposure pre-division tried here previously. That divide-back made
                // sense for DeferredResolvePass's emission term specifically because emission is a
                // flat texture value already authored in final, graded units - dividing it back
                // out before the SAME blanket multiply is undoing double-amplification of a value
                // that was never meant to scale with exposure at all. This forward program's own
                // output is NOT that kind of value - it's real, computed LIGHTING (dot products
                // against the sun direction, hemisphere ambient terms), the same KIND of quantity
                // the deferred G-buffer's own lighting is, which WildRenderingSharp deliberately renders dim
                // and relies on the SAME blanket exposure multiply to bring up to a normal range.
                // Treating this pass's output like emission was suppressing its legitimate
                // brightness right along with it - confirmed by the user's own side-by-side
                // comparison against the real game: the glow rendered here was "way weaker than in
                // game" with the divide-back in place. Safe to remove now that FloorFragmentSource
                // (above) floors the combined result at the real deferred value regardless of this
                // pass's own magnitude - the ORIGINAL reason a compensating scale-down felt
                // necessary (preventing a huge negative value from surviving to blow out downstream)
                // no longer applies; a floored composite can't be corrupted by an extreme value the
                // way a raw additive sum could.
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFuncSeparate(GLEnum.One, GLEnum.One, GLEnum.One, GLEnum.One);
                _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
            }
            _gl.DepthFunc((DepthFunction)sh.RenderState.ResolveDepthFunc());
            _gl.DepthMask(sh.RenderState.DepthWriteEnabled);

            // This redraw depth-tests against depth the G-BUFFER pass already wrote for this
            // exact geometry, using a SEPARATELY COMPILED vertex program (a different decompiled
            // variant than the G-buffer's own, even though both are meant to reconstruct "the
            // same" clip-space position from the same inputs) - two independent shaders computing
            // the same transform via a different instruction sequence can legitimately land a few
            // ULPs apart in IEEE float, and a non-blended (LEQUAL/EQUAL) depth-func shape's test
            // outcome then depends on whether that tiny gap survives the G-buffer depth
            // texture's quantisation - which is coarser at long range than close up. Confirmed:
            // Enemy_MiasmaTentacle's Mt_Skin visibly redraws almost EVERYWHERE at a distance (test
            // passes - coarse quantisation hides the mismatch) but only in scattered patches up
            // close (test fails more often - fine quantisation exposes it), producing a flat wrong
            // colour at range that "clears" on approach as the correctly-shaded G-buffer result
            // shows through more often instead. A small negative polygon offset (pull this draw's
            // depth slightly TOWARD the camera) makes the LEQUAL/EQUAL test reliably pass
            // regardless of that ULP-scale gap, at every distance - real occlusion by genuinely
            // different geometry is orders of magnitude larger than this bias and stays intact.
            // Skipped for blended shapes: they don't redraw the SAME already-resolved silhouette,
            // so there's no matching G-buffer depth to reliably reproduce in the first place.
            if (!sh.Blend)
            {
                _gl.Enable(EnableCap.PolygonOffsetFill);
                _gl.PolygonOffset(-1f, -1f);
            }
            else
            {
                _gl.Disable(EnableCap.PolygonOffsetFill);
            }

            uint program = sh.ForwardProgram;
            if (sh.DebugForwardProgram is { } debugProgram)
            {
                // The step debugger's instrumented program - the uniform is only ever read by
                // it (the real ForwardProgram doesn't declare uDebugStepTarget at all), so this
                // must be set before Draw's UseProgram, not after.
                program = debugProgram;
                _gl.UseProgram(program);
                _gl.SetInt(program, "uDebugStepTarget", sh.DebugStepTarget);
            }
            ShapeDrawing.Draw(_gl, program, sh.ForwardVao, sh.MaterialUboBuffer, sh.ForwardSamplers, sh.IndexCount, sh.SamplerOverrides);
        }
        }

        _gl.Disable(EnableCap.Blend);
        // Restore the blend EQUATION too, not just the enable. GL keeps the equation as global
        // state even while blending is disabled, so a material that authors sub/reverse_sub/min/max
        // (RenderState maps all four) leaves it set for the whole rest of the frame AND every frame
        // after - and the next pass to merely Enable(Blend) without stating its own equation
        // inherits it. That is a silent, cross-pass, cross-frame corruption whose symptom shows up
        // nowhere near this pass; no shipped manifest currently authors a non-add op, which is
        // exactly why it would go unnoticed until one does.
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Disable(EnableCap.DepthTest);
        resources.BindUbo("ctx_true", 1); // restore - everything after this pass expects the true (unflipped) projection
        FlipIntoWithFloor(resources, targets, targets.Final, targets.Scene, targets.Behind, flip: true);
    }

    void BindAt(int unit, uint handle, TextureTarget target = TextureTarget.Texture2D)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(target, handle);
    }

    /// <summary>
    /// <c>cTex_Proc3DNoise</c> - loads the real 3D Worley/Perlin noise volume
    /// (<c>ModelPreparer.EnsureSystemTextures</c>/<c>SystemTextures.ExtractProc3DNoise</c>, a
    /// romfs asset, not a per-frame render target) when it's been extracted, falling back to a
    /// flat mid-grey 1x1x1 placeholder otherwise (an older cache, or extraction hasn't run yet) -
    /// every effect branch that samples this texture is meant to read a spatially-VARYING value;
    /// the flat placeholder made every one of them uniform across an entire surface, which is a
    /// real, visible difference from the intended look (see CLAUDE.md's remarks on the "Ganon
    /// soul" cyan bug this was investigated for).
    /// </summary>
    unsafe uint LoadRealNoiseVolumeOrFallback(GL gl, string? systemTexturesDirectory)
    {
        string? dataPath = systemTexturesDirectory is { } dir ? Path.Combine(dir, "Proc3DNoise.r8") : null;
        string? dimsPath = systemTexturesDirectory is { } dir2 ? Path.Combine(dir2, "Proc3DNoise.dims.txt") : null;

        if (dataPath is not null && dimsPath is not null && File.Exists(dataPath) && File.Exists(dimsPath))
        {
            var parts = File.ReadAllText(dimsPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int w = int.Parse(parts[0]), h = int.Parse(parts[1]), d = int.Parse(parts[2]);
            byte[] raw = File.ReadAllBytes(dataPath);
            if (raw.Length == w * h * d)
            {
                uint handle = gl.GenTexture();
                gl.BindTexture(TextureTarget.Texture3D, handle);
                fixed (byte* ptr = raw)
                    gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.R8, (uint)w, (uint)h, (uint)d, 0, PixelFormat.Red, PixelType.UnsignedByte, ptr);
                // Same broadcast every other BC4-sourced (single-channel) texture gets - see
                // TextureCache.ApplySwizzle's remarks on why an unset swizzle reads red-only.
                gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Red);
                gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);
                gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
                SetRepeatLinear(TextureTarget.Texture3D, handle);
                return handle;
            }
        }

        uint fallback = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture3D, fallback);
        float[] noise = [0.5f, 0.5f, 0.5f, 1f];
        fixed (float* ptr = noise)
            gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.Rgba32f, 1, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, ptr);
        SetRepeatLinear(TextureTarget.Texture3D, fallback);
        return fallback;
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

    unsafe uint CreateConstTexture2DArray(float r, float g, float b, float a, int layers)
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, handle);
        float[] data = new float[4 * layers];
        for (int i = 0; i < layers; i++) { data[i * 4] = r; data[i * 4 + 1] = g; data[i * 4 + 2] = b; data[i * 4 + 3] = a; }
        fixed (float* ptr = data)
            _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba32f, 1, 1, (uint)layers, 0, PixelFormat.Rgba, PixelType.Float, ptr);
        SetRepeatLinear(TextureTarget.Texture2DArray, handle);
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
        _gl.DeleteProgram(_flipProgram);
        _gl.DeleteProgram(_floorProgram);
        _gl.DeleteTexture(_texWhite);
        _gl.DeleteTexture(_texVolumeMask);
        _gl.DeleteTexture(_texNoise3D);
        _gl.DeleteTexture(_texArrayWhite);
        _gl.DeleteTexture(_texShadowCascadeArray);
    }
}
