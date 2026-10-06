using System.Linq;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Turns a <see cref="ModelManifest"/> into a ready-to-draw <see cref="LoadedModel"/> - GL
/// buffers/VAOs/textures/material UBOs for every shape. Mirrors <c>load_shapes</c> (plus
/// <c>model_bounds</c> for the world-space AABB), with no BFRES/BNSH parsing of its own - that
/// already happened offline in <c>ShaderLibrary.CompileTool</c>.
/// </summary>
public sealed class ModelLoader
{
    readonly GL _gl;
    readonly ShaderProgramCache _programs;
    readonly string _dataDirectory;

    readonly ExternalTextures? _external;

    /// <param name="external">A host's shared textures - see <see cref="ExternalTextures"/>.</param>
    public ModelLoader(GL gl, ShaderProgramCache programs, string dataDirectory, ExternalTextures? external = null)
    {
        _gl = gl;
        _programs = programs;
        _dataDirectory = dataDirectory;
        _external = external;
    }

    /// <param name="modelName">The model to load.</param>
    /// <param name="enableKnownDecompilerCorrections">
    /// Mirrors <see cref="Rendering.LightingContext.EnableKnownMaterialFixes"/> ("WildRenderingSharp Related
    /// Improvements" in the UI) - gates <see cref="KnownDecompilerCorrections"/>'s forward-program
    /// regex the same opt-in way as the rest of that category, now that DebugMode shader
    /// translation (the offline decompiler's <c>TranslationFlags.DebugMode</c>) is suspected to fix
    /// the root decompiler bug this regex was patching around - lets the user A/B test "did
    /// DebugMode alone actually fix it" by reloading with this off, without needing a rebuild. Only
    /// takes effect on (re)load, since shader programs are compiled once here, not re-checked per
    /// frame.
    /// </param>
    public LoadedModel Load(string modelName, bool enableKnownDecompilerCorrections = true)
    {
        var manifest = ModelManifest.Load(Path.Combine(_dataDirectory, $"{modelName}.manifest.json"));
        var textures = new TextureCache(_gl, _dataDirectory, _external);
        var shapes = new List<LoadedShape>();
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var allVertexPositions = new List<Vector3>();

        foreach (var sh in manifest.Shapes)
        {
            if (sh.Programs.GBuffer < 0)
            {
                Console.WriteLine($"  [skip] {sh.Name}: no gbuffer program resolved");
                continue;
            }

            byte[] vertexBytes = File.ReadAllBytes(Path.Combine(_dataDirectory, sh.VertexFile));
            AccumulateBounds(vertexBytes, manifest.VertexStride, ref lo, ref hi, allVertexPositions);

            uint vbo = GLBuffer.Create(_gl, BufferTargetARB.ArrayBuffer, vertexBytes);
            uint ibo = GLBuffer.Create(_gl, BufferTargetARB.ElementArrayBuffer,
                File.ReadAllBytes(Path.Combine(_dataDirectory, sh.IndexFile)));

            uint gbufferProgram = _programs.Load(sh.GBufferShader);
            uint gbufferVao = BuildVertexArray(gbufferProgram, manifest.VertexLayout, manifest.VertexStride, vbo, ibo);
            var gbufferSamplers = textures.Resolve(sh.Samplers);

            // Depth prepass (z-only): many materials carry no inline alpha-test discard in their
            // G-buffer program and rely on this variant to do the cut instead - it has its OWN
            // sampler layout (the same texture can sit on a different unit here).
            uint zonlyProgram = 0, zonlyVao = 0;
            IReadOnlyList<ShapeSampler> zonlySamplers = [];
            if (!string.IsNullOrEmpty(sh.ZOnlyShader) && _programs.Exists(sh.ZOnlyShader))
            {
                zonlyProgram = _programs.Load(sh.ZOnlyShader);
                zonlyVao = BuildVertexArray(zonlyProgram, manifest.VertexLayout, manifest.VertexStride, vbo, ibo);
                zonlySamplers = textures.Resolve(sh.ZOnlySamplers);
            }

            // Forward (gsys_assign_material) - built for ANY shape that resolved one, not only
            // blended surfaces. A blended surface has no choice (it can't go through the deferred
            // G-buffer - there's nothing to blend against yet), but this program is NOT merely a
            // blend-only fallback: the real decompiled shader for e.g. Enemy_Bokoblin's opaque
            // Mt_Skin material genuinely reads gsys_material fields no other resolved program
            // touches (p_miasma_ratio/p_kari_mottled_ratio/p_kari_chemical_ice_ratio0/
            // p_proc_vanishing/p_damage_color, ...) to composite status-effect overlays (miasma
            // corruption, ice/chemical status, camouflage mottling, hit-flash, procedural
            // dissolve) on top of the G-buffer's own opaque result - see ForwardPass.Run, which
            // draws this shape depth-EQUAL-tested against the G-buffer depth it was rasterised
            // alongside, using the shape's OWN render state (opaque materials' default blend
            // resolves to a full replace whenever the shader's own alpha constant is 1, which it
            // is for every material checked so far), so an object with no active status effect
            // redraws its own unchanged colour and one that does gets the effect - never gating
            // this on RenderState.Blend, which only ever meant "can this NOT go through the
            // G-buffer," not "does this shape's forward program do anything."
            uint forwardProgram = 0, forwardVao = 0;
            IReadOnlyList<ShapeSampler> forwardSamplers = [];
            if (!string.IsNullOrEmpty(sh.MaterialShader) && _programs.Exists(sh.MaterialShader))
            {
                forwardProgram = _programs.Load(sh.MaterialShader, isForwardProgram: enableKnownDecompilerCorrections);
                forwardVao = BuildVertexArray(forwardProgram, manifest.VertexLayout, manifest.VertexStride, vbo, ibo);
                forwardSamplers = textures.Resolve(sh.MaterialSamplers);
            }

            byte[] materialUbo = File.ReadAllBytes(Path.Combine(_dataDirectory, sh.MaterialUbo));
            uint materialUboBuffer = GLBuffer.CreatePaddedUniformBuffer(_gl, materialUbo);
            var materialParams = MaterialParamLayout.TryLoadBeside(_dataDirectory, sh.MaterialUbo);
            uint passIdVao = BuildPassIdVao(manifest.VertexLayout, manifest.VertexStride, vbo, ibo);

            shapes.Add(new LoadedShape
            {
                Name = sh.Name,
                Material = sh.Material,
                DeferredPass = sh.DeferredPass,
                AlphaTest = sh.AlphaTest,
                Blend = sh.RenderState.Blend,
                RenderState = sh.RenderState,
                VertexBuffer = vbo,
                IndexBuffer = ibo,
                IndexCount = sh.IndexCount,
                Lods = sh.Lods is { Count: > 0 } lods
                    ? [.. lods.Where(l => l.Length == 2).Select(l => (l[0], l[1]))]
                    : [(0, sh.IndexCount)],
                VertexSkinCount = sh.VertexSkinCount,
                GBufferProgram = gbufferProgram,
                GBufferVao = gbufferVao,
                GBufferSamplers = gbufferSamplers,
                ZOnlyProgram = zonlyProgram,
                ZOnlyVao = zonlyVao,
                ZOnlySamplers = zonlySamplers,
                ForwardProgram = forwardProgram,
                ForwardVao = forwardVao,
                ForwardSamplers = forwardSamplers,
                ForwardShaderName = forwardProgram != 0 ? sh.MaterialShader : "",
                GBufferShaderName = sh.GBufferShader,
                ZOnlyShaderName = zonlyProgram != 0 ? sh.ZOnlyShader : "",
                MaterialUboBuffer = materialUboBuffer,
                MaterialUboBytes = materialUbo,
                MaterialParams = materialParams,
                PassIdVao = passIdVao,
            });
            Console.WriteLine($"  {sh.Name}: {sh.GBufferShader}, {gbufferSamplers.Count} textures, pass={sh.DeferredPass}");
        }

        if (shapes.Count == 0)
            throw new InvalidOperationException($"Manifest '{modelName}' resolved no drawable shapes.");

        string skeletonPath = Path.Combine(_dataDirectory, $"{modelName}.skeleton.json");
        SkeletonManifest? skeleton = File.Exists(skeletonPath) ? SkeletonManifest.Load(skeletonPath) : null;
        var availableAnims = SkeletalAnimManifest.ListAvailable(_dataDirectory, modelName).ToList();
        var availableTexturePatternAnims = TexturePatternAnimManifest.ListAvailable(_dataDirectory, modelName).ToList();
        var availableMaterialAnims = MaterialAnimManifest.ListAvailable(_dataDirectory, modelName).ToList();

        return new LoadedModel(_gl, textures)
        {
            Manifest = manifest,
            Shapes = shapes,
            BoundsMin = lo,
            BoundsMax = hi,
            VertexPositions = allVertexPositions,
            Skeleton = skeleton,
            DataDirectory = _dataDirectory,
            AvailableAnims = availableAnims,
            AvailableTexturePatternAnims = availableTexturePatternAnims,
            AvailableMaterialAnims = availableMaterialAnims,
        };
    }

    /// <summary>Position is always the vertex's first vec4 (see <c>ExportTestBench</c>'s fixed interleaved layout) regardless of which attributes a given program actually samples.</summary>
    static void AccumulateBounds(byte[] vertexBytes, int stride, ref Vector3 lo, ref Vector3 hi, List<Vector3> allPositions)
    {
        int count = vertexBytes.Length / stride;
        var span = vertexBytes.AsSpan();
        for (int i = 0; i < count; i++)
        {
            var v = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Vector3>(span.Slice(i * stride, 12))[0];
            lo = Vector3.Min(lo, v);
            hi = Vector3.Max(hi, v);
            allPositions.Add(v);
        }
    }

    /// <summary>
    /// Binds each attribute the manifest's fixed layout describes at whichever location THIS
    /// linked program actually assigned it (queried by name), padding over everything the
    /// program doesn't use - a decompiled/linked program silently drops an input its code never
    /// reads, and binding a name the program lacks is undefined. Mirrors <c>build_vertex_format</c>.
    /// </summary>
    unsafe uint BuildVertexArray(uint program, List<VertexLayoutEntry> layout, int stride, uint vbo, uint ibo)
    {
        uint vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);

        foreach (var entry in layout.OrderBy(e => e.Offset))
        {
            int location = _gl.GetAttribLocation(program, entry.Name);
            if (location < 0)
                continue;
            _gl.EnableVertexAttribArray((uint)location);
            _gl.VertexAttribPointer((uint)location, entry.Components, VertexAttribPointerType.Float,
                false, (uint)stride, (void*)(nint)entry.Offset);
        }

        _gl.BindVertexArray(0);
        return vao;
    }

    /// <summary>Position and the four blend attributes the pass-ID stamp skins with, at the FIXED locations <see cref="Pipeline.PassIdMaskPass"/>'s own shader declares - which are the same locations the manifest layout already assigns them, so they are looked up by name rather than hardcoded here. The stamp needs the same silhouette and depth as the real draw, which since skinning moved to the GPU means it has to skin too; everything else in the vertex is skipped.</summary>
    static readonly string[] PassIdAttributes = ["aPosition", "aBlendWeight0", "aBlendWeight1", "aBlendIndex0", "aBlendIndex1"];

    unsafe uint BuildPassIdVao(List<VertexLayoutEntry> layout, int stride, uint vbo, uint ibo)
    {
        uint vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);

        // Blend indices are integers stored in the float attribute's bit pattern, which is why they
        // are read as Float here and unpacked with floatBitsToInt in the shader - the same thing
        // BuildVertexArray does for the real programs.
        foreach (var entry in layout)
        {
            if (Array.IndexOf(PassIdAttributes, entry.Name) < 0)
                continue;
            _gl.EnableVertexAttribArray((uint)entry.Location);
            _gl.VertexAttribPointer((uint)entry.Location, entry.Components, VertexAttribPointerType.Float,
                false, (uint)stride, (void*)(nint)entry.Offset);
        }

        _gl.BindVertexArray(0);
        return vao;
    }
}
