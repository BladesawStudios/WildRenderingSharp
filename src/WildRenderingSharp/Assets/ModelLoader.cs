using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Turns a <see cref="ModelManifest"/> into a ready-to-draw <see cref="LoadedModel"/>: GL buffers, VAOs, textures and material
/// buffers for every shape. It parses no BFRES or BNSH itself; that happened offline in <c>ShaderLibrary.CompileTool</c>.
/// </summary>
public sealed class ModelLoader
{
    readonly GL _gl;
    readonly ShaderProgramCache _programs;
    readonly string _dataDirectory;

    readonly ExternalTextures? _external;

    /// <summary>Textures shared with other models (see <see cref="Assets.SharedTextures"/>); null loads this model's own.</summary>
    public SharedTextures? SharedTextures { get; init; }

    public ModelLoader(GL gl, ShaderProgramCache programs, string dataDirectory, ExternalTextures? external = null)
    {
        _gl = gl;
        _programs = programs;
        _dataDirectory = dataDirectory;
        _external = external;
    }

    /// <summary>
    /// Uploads only what the model's programs read, for a model drawn only in its bind pose, such as a map's static objects.
    /// Attributes no program of a shape reads are left out of its vertex buffer (the exported vertex carries every attribute, 192
    /// bytes).
    /// </summary>
    public bool CompactVertices { get; init; }

    /// <summary>
    /// Leaves the vertex arrays to <see cref="LoadedModel.FinishOnRenderThread"/>, for a load on a worker thread with its own
    /// context: buffers, textures and programs are shared between contexts, vertex arrays are not.
    /// </summary>
    public bool DeferVertexArrays { get; init; }

    public LoadedModel Load(string modelName, bool enableKnownDecompilerCorrections = true)
    {
        var manifest = ModelManifest.Load(Path.Combine(_dataDirectory, $"{modelName}.manifest.json"));
        var textures = new TextureCache(_gl, _dataDirectory, _external, SharedTextures);
        var shapes = new List<LoadedShape>();
        var pending = new List<Action>();
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

            var vaoLayout = layout;
            int vaoStride = stride;
            bool vaoConstantSkin = constantSkin;
            uint MakeVao(uint program) => BuildVertexArray(program, vaoLayout, vaoStride, vbo, ibo, vaoConstantSkin);
            uint gbufferVao = DeferVertexArrays ? 0 : MakeVao(gbufferProgram);
            var gbufferSamplers = textures.Resolve(sh.Samplers);

            // Depth prepass (z-only): many materials carry no inline alpha-test discard in their G-buffer program and rely on this variant to cut, and it has its own sampler layout.
            uint zonlyVao = 0;
            IReadOnlyList<ShapeSampler> zonlySamplers = [];
            if (zonlyProgram != 0)
            {
                zonlyVao = DeferVertexArrays ? 0 : MakeVao(zonlyProgram);
                zonlySamplers = textures.Resolve(sh.ZOnlySamplers);
            }

            // Forward (gsys_assign_material), built for any shape that resolved one, not only blended surfaces. A blended surface has no
            // choice, but this program is not a blend-only fallback: opaque materials such as Enemy_Bokoblin's Mt_Skin read gsys_material
            // fields no other program touches (p_miasma_ratio, p_damage_color, p_proc_vanishing, ...) to composite status effects over
            // the G-buffer result. ForwardPass draws it depth-EQUAL against the G-buffer depth with the shape's own render state, so an
            // object with no active effect redraws its unchanged colour. Never gate this on RenderState.Blend, which only means
            // "cannot go through the G-buffer".
            uint forwardVao = 0;
            IReadOnlyList<ShapeSampler> forwardSamplers = [];
            if (forwardProgram != 0)
            {
                forwardVao = DeferVertexArrays ? 0 : MakeVao(forwardProgram);
                forwardSamplers = textures.Resolve(sh.MaterialSamplers);
            }

            byte[] materialUbo = File.ReadAllBytes(Path.Combine(_dataDirectory, sh.MaterialFile));
            uint materialUboBuffer = GLBuffer.CreatePaddedUniformBuffer(_gl, materialUbo);
            var materialParams = MaterialParamLayout.TryLoadBeside(_dataDirectory, sh.MaterialFile);
            uint passIdVao = DeferVertexArrays ? 0 : BuildPassIdVao(layout, stride, vbo, ibo, constantSkin);

            // The material's own static options, exported beside its geometry.
            string optionsPath = Path.Combine(_dataDirectory, $"{sh.Name}_options.txt");
            bool hideNormalPass = File.Exists(optionsPath) && File.ReadLines(optionsPath)
                .Any(l => l.Trim().Equals("o_enable_hide_normal_pass=True", StringComparison.OrdinalIgnoreCase));
            bool noTextures = sh.Samplers.Count == 0;

            var shape = new LoadedShape
            {
                Hidden = hideNormalPass || noTextures,
                CastsShadow = !hideNormalPass && sh.RenderState.DepthWriteEnabled,
                Name = sh.Name,
                Material = sh.Material,
                Tags = sh.Extensions.Where(e => e.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    .ToDictionary(e => e.Key, e => e.Value.ToString()),
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
                MaterialBuffer = materialUboBuffer,
                MaterialBytes = materialUbo,
                MaterialParams = materialParams,
                PassIdVao = passIdVao,
            };
            shapes.Add(shape);
            if (DeferVertexArrays)
            {
                uint zp = zonlyProgram, fp = forwardProgram, gp = gbufferProgram;
                pending.Add(() =>
                {
                    shape.GBufferVao = MakeVao(gp);
                    if (zp != 0)
                        shape.ZOnlyVao = MakeVao(zp);
                    if (fp != 0)
                        shape.ForwardVao = MakeVao(fp);
                    shape.PassIdVao = BuildPassIdVao(vaoLayout, vaoStride, vbo, ibo, vaoConstantSkin);
                });
            }
            Console.WriteLine($"  {sh.Name}: {sh.GBufferShader}, {gbufferSamplers.Count} textures");
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
            PendingVertexArrays = pending.Count > 0 ? pending : null,
        };
    }

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

    // Binds each attribute of the manifest's fixed layout at its location, for every location this linked program reads (a
    // linked program drops inputs its code never reads). Matched by location, not name: the programs declare inputs at the
    // exporter's locations but not always under its names (location 3 is aU254 to the layout and aTexCoordBake to 356 programs;
    // location 10 is aTexCoord2 to some and aNormal0 to others).
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

    // The attributes a shape's vertex buffer must carry: whatever any of its programs reads, plus position and, for a skinned
    // shape, the blend attributes the pass-ID stamp skins with - less those, when they are replaced by the shared constant
    // vertex.
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

    // Binds the blend attributes to one shared vertex holding all weight on palette slot 0, through a divisor no instance count
    // reaches so every vertex of every instance reads element 0 (the instancing shaders address instances through their own
    // uniform, never a base instance).
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

    // Position and the four blend attributes the pass-ID stamp skins with, at the locations the pass-ID shader declares (the
    // manifest layout already assigns them, so they are found by name). The stamp needs the same silhouette and depth as the
    // real draw, so it has to skin too.
    static readonly string[] PassIdAttributes = ["aPosition", "aBlendWeight0", "aBlendWeight1", "aBlendIndex0", "aBlendIndex1"];

    unsafe uint BuildPassIdVao(List<VertexLayoutEntry> layout, int stride, uint vbo, uint ibo, bool constantSkin = false)
    {
        uint vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);

        // Blend indices are integers in the float attribute's bit pattern: read as Float here and unpacked with floatBitsToInt in the shader, as BuildVertexArray does.
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

    static int PassIdLocation(string name) => name switch
    {
        "aBlendWeight0" => 4,
        "aBlendWeight1" => 5,
        "aBlendIndex0" => 6,
        "aBlendIndex1" => 7,
        _ => -1,
    };
}
