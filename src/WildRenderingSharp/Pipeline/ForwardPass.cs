using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Draws blended materials forward, after the deferred resolve: a blended surface cannot go through the G-buffer, so the engine
/// draws it with <c>gsys_assign_material</c> once the scene behind it is resolved.
/// </summary>
public sealed class ForwardPass : IDisposable
{
    readonly GL _gl;
    readonly uint _flipProgram, _floorProgram;
    readonly uint _texWhite, _texVolumeMask, _texNoise3D, _texArrayWhite, _texShadowCascadeArray;
    static readonly int[] WhiteNeutralUnits = [7, 11, 13, 14, 15, 31];

    static readonly string FlipFragmentSource = GlslFiles.Load("Pipeline/Forward/Flip.frag");

    // Used only for the final Scene to Final step, after forward geometry has been additively drawn into
    // Scene. Floors the combined result at tFloor (Behind, the deferred-only content captured before any
    // forward drawing). The forward program's "base colour" term is negative for every input
    // (material_prog10336's temp_102 has the "<var> * 0.0" shape of an already-documented decompiler
    // artifact), and no fixed floor survives a channel whose delta exceeds the deferred value (deferred red
    // 0.0018 against a forward delta of -0.0812). Flooring against the real deferred value means the forward
    // pass can only brighten a pixel, never darken it below what the G-buffer computed.
    static readonly string FloorFragmentSource = GlslFiles.Load("Pipeline/Forward/Floor.frag");

    public unsafe ForwardPass(GL gl, string? systemTexturesDirectory = null)
    {
        _gl = gl;
        _flipProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FlipFragmentSource, "flip_blit");
        _floorProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FloorFragmentSource, "flip_blit_floor");

        _texWhite = CreateConstTexture2D(1, 1, 1, 1);
        _texVolumeMask = CreateConstTexture2D(0, 0, 0, 0);
        _texArrayWhite = CreateConstTexture2DArray(1, 1, 1, 1, layers: 1);

        _texNoise3D = LoadRealNoiseVolumeOrFallback(gl, systemTexturesDirectory);

        // A 1x1x1 depth array with comparison on and depth 1.0 (LEQUAL), meaning nothing occludes: the neutral
        // value for cTex_DepthShadowCascade, which the forward shader samples (an unbound sampler of the wrong
        // type reads as undefined and produced NaN across the frame).
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

    public void FlipIntoWithFloor(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, GpuTexture floorTex, bool flip)
    {
        _gl.UseProgram(_floorProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_floorProgram, "t", 0, src.Handle);
        _gl.BindTextureUniform(_floorProgram, "tFloor", 1, floorTex.Handle);
        _gl.SetInt(_floorProgram, "uFlip", flip ? 1 : 0);
        resources.DrawFullscreenTriangle();
    }

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShaderProgramCache programs)
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

        // The forward geometry is rasterised into Scene and GBufferDepth in the G-buffer's flipped orientation,
        // so the shared camera block must be the flipped one: otherwise the shape is transformed unflipped while
        // everything around it is flipped, and the final flip-back mirrors only the newly drawn geometry.
        resources.BindCamera(FrameUniformKeys.GBufferCamera);
        ClipOrigin.Game(_gl, true);

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
                // Opaque and masked shapes redraw here for their status-effect overlay, additively. Traced end
                // to end, Mt_Skin's forward program (material_prog10336) has a base colour term that is negative
                // for every input, so its output is a delta to composite over the deferred result, not a
                // replacement: replacing discarded the lit pixel and left a flat black hand with a glowing rim.
                // No exposure pre-division: this output is real lighting, the same kind of quantity the deferred
                // path produces and exposes with the same blanket multiply, unlike emission, which is authored
                // in final units. Dividing it back made the glow far weaker than in game.
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFuncSeparate(GLEnum.One, GLEnum.One, GLEnum.One, GLEnum.One);
                _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
            }
            _gl.DepthFunc((DepthFunction)sh.RenderState.ResolveDepthFunc());
            _gl.DepthMask(sh.RenderState.DepthWriteEnabled);

            // This redraw depth-tests against depth the G-buffer pass wrote for the same geometry, using a
            // separately compiled vertex program, and two shaders computing one transform can land a few ULPs
            // apart. Whether that gap survives the depth texture's quantisation varies with distance (the surface
            // redrew almost everywhere at range but only in patches up close). A small negative polygon offset
            // pulls this draw toward the camera so the LEQUAL/EQUAL test passes at any distance; real occlusion
            // is orders of magnitude larger. Skipped for blended shapes, which do not redraw an already-resolved silhouette.
            if (!sh.Blend)
            {
                _gl.Enable(EnableCap.PolygonOffsetFill);
                _gl.PolygonOffset(-1f, -1f);
            }
            else
            {
                _gl.Disable(EnableCap.PolygonOffsetFill);
            }

            if (group.Batch is not null)
            {
                group.Draw(_gl, programs, sh, ShapeProgram.Forward);
                continue;
            }
            uint program = sh.ForwardProgram;
            if (sh.DebugForwardProgram is { } debugProgram)
            {
                // The step debugger's program declares uDebugStepTarget and the real one does not, so it is set before Draw's UseProgram.
                program = debugProgram;
                _gl.UseProgram(program);
                _gl.SetInt(program, "uDebugStepTarget", sh.DebugStepTarget);
            }
            ShapeDrawing.Draw(_gl, programs.Bindings.Material, program, sh.ForwardVao, sh.MaterialBuffer, sh.ForwardSamplers, sh.IndexCount, sh.SamplerOverrides);
        }
        }

        _gl.Disable(EnableCap.Blend);
        // Restore the blend equation too: GL keeps it as global state even with blending disabled, so a
        // material authoring sub, min or max would leak it into every later pass and frame.
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Disable(EnableCap.DepthTest);
        ClipOrigin.Game(_gl, false);
        resources.BindCamera(FrameUniformKeys.SceneCamera); // later passes expect the unflipped projection
        FlipIntoWithFloor(resources, targets, targets.Final, targets.Scene, targets.Behind, flip: true);
    }

    void BindAt(int unit, uint handle, TextureTarget target = TextureTarget.Texture2D)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(target, handle);
    }

    // cTex_Proc3DNoise: the 3D Worley and Perlin noise volume extracted from romfs when available, else a flat mid-grey
    // placeholder. Every effect branch sampling it expects a spatially varying value; the placeholder makes them uniform.
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
                // Broadcast like every other single-channel texture (see TextureCache.ApplySwizzle).
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
