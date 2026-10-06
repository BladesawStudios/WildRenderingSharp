using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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

    /// <summary>Textures shared with other models (see <see cref="Assets.SharedTextures"/>); null loads this model's own.</summary>
    public SharedTextures? SharedTextures { get; init; }

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
    /// <summary>
    /// Uploads only what the model's own programs read, for a model that is only ever drawn
    /// standing still in its bind pose - a map's worth of static objects. Every attribute no
    /// program of a shape reads is left out of its vertex buffer (the exported vertex carries every
    /// attribute any program might want, 192 bytes of floats). A smooth-skinned shape (two or more
    /// weights per vertex) also drops its blend weights and indices when the model's bind-pose
    /// palette is the identity in every smooth slot, which it is for a model whose inverse binds
    /// match its skeleton: every vertex then lands on the same matrix whatever its weights say, so
    /// they are replaced by one shared "all on slot 0" vertex. A shape that keeps its weights
    /// (one weight per vertex, or a palette that is not the identity) keeps them.
    /// </summary>
    /// <remarks>Never for a model that will be posed or animated - its weights are gone.</remarks>
    public bool CompactVertices { get; init; }

    public LoadedModel Load(string modelName, bool enableKnownDecompilerCorrections = true)
    {
        var manifest = ModelManifest.Load(Path.Combine(_dataDirectory, $"{modelName}.manifest.json"));
        var textures = new TextureCache(_gl, _dataDirectory, _external, SharedTextures);
        var shapes = new List<LoadedShape>();
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var allVertexPositions = new List<Vector3>();

        string skeletonPath = Path.Combine(_dataDirectory, $"{modelName}.skeleton.json");
        SkeletonManifest? skeleton = File.Exists(skeletonPath) ? SkeletonManifest.Load(skeletonPath) : null;
        bool weightsInert = CompactVertices && SmoothPaletteIsIdentity(skeleton);

        foreach (var sh in manifest.Shapes)
        {
            if (sh.Programs.GBuffer < 0)
            {
                Console.WriteLine($"  [skip] {sh.Name}: no gbuffer program resolved");
                continue;
            }

            byte[] vertexBytes = File.ReadAllBytes(Path.Combine(_dataDirectory, sh.VertexFile));
            AccumulateBounds(vertexBytes, manifest.VertexStride, ref lo, ref hi, allVertexPositions);

            uint gbufferProgram = _programs.Load(sh.GBufferShader);
            uint zonlyProgram = 0, forwardProgram = 0;
            if (!string.IsNullOrEmpty(sh.ZOnlyShader) && _programs.Exists(sh.ZOnlyShader))
                zonlyProgram = _programs.Load(sh.ZOnlyShader);
            if (!string.IsNullOrEmpty(sh.MaterialShader) && _programs.Exists(sh.MaterialShader))
                forwardProgram = _programs.Load(sh.MaterialShader, isForwardProgram: enableKnownDecompilerCorrections);

            List<VertexLayoutEntry> layout = manifest.VertexLayout;
            int stride = manifest.VertexStride;
            bool constantSkin = false;
            if (CompactVertices)
            {
                constantSkin = weightsInert && sh.VertexSkinCount >= 2;
                (layout, stride, vertexBytes) = Compact(manifest.VertexLayout, manifest.VertexStride, vertexBytes,
                    UsedAttributes(manifest.VertexLayout, sh.VertexSkinCount, constantSkin, gbufferProgram, zonlyProgram, forwardProgram));
            }

            uint vbo = GLBuffer.Create(_gl, BufferTargetARB.ArrayBuffer, vertexBytes);
            uint ibo = GLBuffer.Create(_gl, BufferTargetARB.ElementArrayBuffer,
                File.ReadAllBytes(Path.Combine(_dataDirectory, sh.IndexFile)));

            uint gbufferVao = BuildVertexArray(gbufferProgram, layout, stride, vbo, ibo, constantSkin);
            var gbufferSamplers = textures.Resolve(sh.Samplers);

            // Depth prepass (z-only): many materials carry no inline alpha-test discard in their
            // G-buffer program and rely on this variant to do the cut instead - it has its OWN
            // sampler layout (the same texture can sit on a different unit here).
            uint zonlyVao = 0;
            IReadOnlyList<ShapeSampler> zonlySamplers = [];
            if (zonlyProgram != 0)
            {
                zonlyVao = BuildVertexArray(zonlyProgram, layout, stride, vbo, ibo, constantSkin);
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
            uint forwardVao = 0;
            IReadOnlyList<ShapeSampler> forwardSamplers = [];
            if (forwardProgram != 0)
            {
                forwardVao = BuildVertexArray(forwardProgram, layout, stride, vbo, ibo, constantSkin);
                forwardSamplers = textures.Resolve(sh.MaterialSamplers);
            }

            byte[] materialUbo = File.ReadAllBytes(Path.Combine(_dataDirectory, sh.MaterialUbo));
            uint materialUboBuffer = GLBuffer.CreatePaddedUniformBuffer(_gl, materialUbo);
            var materialParams = MaterialParamLayout.TryLoadBeside(_dataDirectory, sh.MaterialUbo);
            uint passIdVao = BuildPassIdVao(layout, stride, vbo, ibo, constantSkin);

            // The material's own static options, exported beside its geometry.
            string optionsPath = Path.Combine(_dataDirectory, $"{sh.Name}_options.txt");
            bool hideNormalPass = File.Exists(optionsPath) && File.ReadLines(optionsPath)
                .Any(l => l.Trim().Equals("o_enable_hide_normal_pass=True", StringComparison.OrdinalIgnoreCase));
            bool noTextures = sh.Samplers.Count == 0;

            shapes.Add(new LoadedShape
            {
                Hidden = hideNormalPass || noTextures,
                CastsShadow = !hideNormalPass,
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
                ReadsSceneColor = _gl.GetUniformLocation(gbufferProgram, "cTex_ColorBuffer") >= 0,
                MaterialUboBuffer = materialUboBuffer,
                MaterialUboBytes = materialUbo,
                MaterialParams = materialParams,
                PassIdVao = passIdVao,
            });
            Console.WriteLine($"  {sh.Name}: {sh.GBufferShader}, {gbufferSamplers.Count} textures, pass={sh.DeferredPass}");
        }

        if (shapes.Count == 0)
            throw new InvalidOperationException($"Manifest '{modelName}' resolved no drawable shapes.");

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
    /// Binds each attribute the manifest's fixed layout describes at its location, for every
    /// location THIS linked program actually reads - a linked program drops an input its code
    /// never reads. Matched by location, not name: every decompiled program declares its inputs
    /// at explicit locations that are the exporter's layout, but not always under the layout's
    /// names. Location 3 is the layout's <c>aU254</c> and 356 programs' <c>aTexCoordBake</c> (the
    /// baked-lighting UV), location 10 is <c>aTexCoord2</c> to some and <c>aNormal0</c> to others -
    /// matched by name, those were never bound at all. Mirrors <c>build_vertex_format</c>.
    /// </summary>
    unsafe uint BuildVertexArray(uint program, List<VertexLayoutEntry> layout, int stride, uint vbo, uint ibo, bool constantSkin = false)
    {
        uint vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);

        var active = ActiveLocations(program);
        foreach (var entry in layout.OrderBy(e => e.Offset))
        {
            if (!active.Contains(entry.Location))
                continue;
            _gl.EnableVertexAttribArray((uint)entry.Location);
            _gl.VertexAttribPointer((uint)entry.Location, entry.Components, VertexAttribPointerType.Float,
                false, (uint)stride, (void*)(nint)entry.Offset);
        }
        if (constantSkin)
            BindConstantSkin(name => _gl.GetAttribLocation(program, name));

        _gl.BindVertexArray(0);
        return vao;
    }

    static readonly string[] BlendAttributes = ["aBlendWeight0", "aBlendWeight1", "aBlendIndex0", "aBlendIndex1"];

    /// <summary>The locations of every vertex input <paramref name="program"/> actually reads.</summary>
    HashSet<int> ActiveLocations(uint program)
    {
        var locations = new HashSet<int>();
        _gl.GetProgram(program, ProgramPropertyARB.ActiveAttributes, out int count);
        for (uint i = 0; i < count; i++)
        {
            string name = _gl.GetActiveAttrib(program, i, out _, out _);
            int location = _gl.GetAttribLocation(program, name);
            if (location >= 0)
                locations.Add(location);
        }
        return locations;
    }

    /// <summary>The attributes a shape's vertex buffer must carry: whatever any of its programs reads, plus position and, for a skinned shape, the blend attributes the pass-ID stamp skins with - less those, when they are replaced by the shared constant vertex.</summary>
    HashSet<string> UsedAttributes(List<VertexLayoutEntry> layout, int skinCount, bool constantSkin, params uint[] programs)
    {
        var used = new HashSet<string> { "aPosition" };
        foreach (uint program in programs)
        {
            if (program == 0)
                continue;
            var active = ActiveLocations(program);
            foreach (var entry in layout)
                if (active.Contains(entry.Location))
                    used.Add(entry.Name);
        }
        if (skinCount >= 1)
            used.UnionWith(BlendAttributes);
        if (constantSkin)
            used.ExceptWith(BlendAttributes);
        return used;
    }

    /// <summary>The vertices with only the <paramref name="used"/> attributes, packed in their original order.</summary>
    static (List<VertexLayoutEntry> Layout, int Stride, byte[] Bytes) Compact(List<VertexLayoutEntry> layout, int stride, byte[] bytes, HashSet<string> used)
    {
        var kept = layout.Where(e => used.Contains(e.Name)).OrderBy(e => e.Offset).ToList();
        var packed = new List<VertexLayoutEntry>(kept.Count);
        int newStride = 0;
        foreach (var e in kept)
        {
            packed.Add(new VertexLayoutEntry { Name = e.Name, Location = e.Location, Offset = newStride, Components = e.Components });
            newStride += e.Components * sizeof(float);
        }
        if (newStride == stride)
            return (layout, stride, bytes);

        int count = bytes.Length / stride;
        var result = new byte[count * newStride];
        for (int v = 0; v < count; v++)
            for (int a = 0; a < kept.Count; a++)
                System.Buffer.BlockCopy(bytes, v * stride + kept[a].Offset, result, v * newStride + packed[a].Offset, kept[a].Components * sizeof(float));
        return (packed, newStride, result);
    }

    /// <summary>
    /// Binds the blend attributes to one shared vertex holding all weight on palette slot 0, fed
    /// through a divisor no instance count reaches, so every vertex of every instance reads the same
    /// element. The instancing shaders address instances through their own uniform, never through a
    /// base instance, so element 0 is always the one read.
    /// </summary>
    unsafe void BindConstantSkin(Func<string, int> location)
    {
        uint buffer = ConstantSkinBuffer(_gl);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        for (int i = 0; i < BlendAttributes.Length; i++)
        {
            int at = location(BlendAttributes[i]);
            if (at < 0)
                continue;
            _gl.EnableVertexAttribArray((uint)at);
            _gl.VertexAttribPointer((uint)at, 4, VertexAttribPointerType.Float, false, 0, (void*)(nint)(i * 16));
            _gl.VertexAttribDivisor((uint)at, uint.MaxValue);
        }
    }

    /// <summary>The one constant-skin vertex per GL context, made on first use and kept for the context's life (64 bytes).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GL, StrongBox<uint>> ConstantSkinBuffers = new();

    static uint ConstantSkinBuffer(GL gl)
    {
        var box = ConstantSkinBuffers.GetValue(gl, _ => new StrongBox<uint>());
        if (box.Value == 0)
        {
            float[] skin =
            [
                1, 0, 0, 0, // aBlendWeight0
                0, 0, 0, 0, // aBlendWeight1
                0, 0, 0, 0, // aBlendIndex0 - integer 0 in a float's bits
                0, 0, 0, 0, // aBlendIndex1
            ];
            box.Value = GLBuffer.Create(gl, BufferTargetARB.ArrayBuffer, System.Runtime.InteropServices.MemoryMarshal.AsBytes(skin.AsSpan()).ToArray());
        }
        return box.Value;
    }

    /// <summary>True when every smooth slot of the bind-pose palette is the identity - see <see cref="CompactVertices"/>.</summary>
    static bool SmoothPaletteIsIdentity(SkeletonManifest? skeleton)
    {
        if (skeleton is null)
            return true;
        Matrix4x4[] palette = InstanceBatch.BindPalette(skeleton);
        int smooth = Math.Min(skeleton.InverseModelMatricesAsMatrices().Length, palette.Length);
        for (int i = 0; i < smooth; i++)
        {
            Matrix4x4 m = palette[i];
            Matrix4x4 identity = Matrix4x4.Identity;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    if (MathF.Abs(m[r, c] - identity[r, c]) > 1e-3f)
                        return false;
        }
        return true;
    }

    /// <summary>Position and the four blend attributes the pass-ID stamp skins with, at the FIXED locations <see cref="Pipeline.PassIdMaskPass"/>'s own shader declares - which are the same locations the manifest layout already assigns them, so they are looked up by name rather than hardcoded here. The stamp needs the same silhouette and depth as the real draw, which since skinning moved to the GPU means it has to skin too; everything else in the vertex is skipped.</summary>
    static readonly string[] PassIdAttributes = ["aPosition", "aBlendWeight0", "aBlendWeight1", "aBlendIndex0", "aBlendIndex1"];

    unsafe uint BuildPassIdVao(List<VertexLayoutEntry> layout, int stride, uint vbo, uint ibo, bool constantSkin = false)
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
        if (constantSkin)
            BindConstantSkin(PassIdLocation);

        _gl.BindVertexArray(0);
        return vao;
    }

    /// <summary>The pass-ID shader's own fixed blend locations (<see cref="Pipeline.PassIdMaskPass"/>).</summary>
    static int PassIdLocation(string name) => name switch
    {
        "aBlendWeight0" => 4,
        "aBlendWeight1" => 5,
        "aBlendIndex0" => 6,
        "aBlendIndex1" => 7,
        _ => -1,
    };
}
