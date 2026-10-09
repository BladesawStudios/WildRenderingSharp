using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Assets.Loading;

// How a model's shapes are built: compacted vertices, vertex arrays left for the GL thread, and the forward programs' decompiler corrections.
readonly record struct ShapeBuildOptions(bool CompactVertices, bool DeferVertexArrays, bool ForwardCorrections);

// The programs a shape draws with; zero where the shape has no such variant.
readonly record struct ShapePrograms(uint GBuffer, uint ZOnly, uint Forward);

/// <summary>Builds the shapes of one model from its manifest: programs, buffers, samplers, material block and vertex arrays.</summary>
sealed class ShapeBuilder(GL gl, ShaderProgramCache programs, string dataDirectory, TextureCache textures, ModelManifest manifest,
    ShapeBuildOptions options, bool weightsInert)
{
    readonly VertexArrays _arrays = new(gl);

    public ModelBounds Bounds { get; } = new();

    // Vertex arrays whose creation waits for the GL thread, one action per shape.
    public List<Action> PendingVertexArrays { get; } = [];

    // Null when the manifest resolved no G-buffer program for the shape, which leaves it undrawable.
    public LoadedShape? Build(ShapeManifestEntry sh)
    {
        if (sh.Programs.GBuffer < 0)
        {
            Console.WriteLine($"  [skip] {sh.Name}: no gbuffer program resolved");
            return null;
        }

        byte[] vertexBytes = File.ReadAllBytes(Path.Combine(dataDirectory, sh.VertexFile));
        Bounds.Add(vertexBytes, manifest.VertexStride);

        var shapePrograms = LinkPrograms(sh);
        var buffers = CreateBuffers(sh, vertexBytes, shapePrograms);
        var shape = Assemble(sh, shapePrograms, buffers);

        if (options.DeferVertexArrays)
            PendingVertexArrays.Add(() => CreateVertexArrays(shape, buffers));
        else
            CreateVertexArrays(shape, buffers);
        return shape;
    }

    ShapePrograms LinkPrograms(ShapeManifestEntry sh)
    {
        uint zOnly = !string.IsNullOrEmpty(sh.ZOnlyShader) && programs.Exists(sh.ZOnlyShader) ? programs.Load(sh.ZOnlyShader) : 0;
        uint forward = !string.IsNullOrEmpty(sh.MaterialShader) && programs.Exists(sh.MaterialShader)
            ? programs.Load(sh.MaterialShader, isForwardProgram: options.ForwardCorrections) : 0;
        return new ShapePrograms(programs.Load(sh.GBufferShader), zOnly, forward);
    }

    ShapeBuffers CreateBuffers(ShapeManifestEntry sh, byte[] vertexBytes, ShapePrograms shapePrograms)
    {
        var layout = manifest.VertexLayout;
        int stride = manifest.VertexStride;
        bool constantSkin = false;
        if (options.CompactVertices)
        {
            constantSkin = weightsInert && sh.VertexSkinCount >= 2;
            var active = new[] { shapePrograms.GBuffer, shapePrograms.ZOnly, shapePrograms.Forward }.Where(p => p != 0).Select(_arrays.ActiveLocations);
            var used = VertexCompactor.UsedAttributes(layout, sh.VertexSkinCount, constantSkin, active);
            (layout, stride, vertexBytes) = VertexCompactor.Compact(layout, stride, vertexBytes, used);
        }

        uint vbo = GLBuffer.Create(gl, BufferTargetARB.ArrayBuffer, vertexBytes);
        uint ibo = GLBuffer.Create(gl, BufferTargetARB.ElementArrayBuffer, File.ReadAllBytes(Path.Combine(dataDirectory, sh.IndexFile)));
        return new ShapeBuffers(layout, stride, vbo, ibo, constantSkin);
    }

    LoadedShape Assemble(ShapeManifestEntry sh, ShapePrograms shapePrograms, ShapeBuffers buffers)
    {
        var shapeOptions = ShapeOptions.Read(sh, dataDirectory);
        return new LoadedShape
        {
            Hidden = shapeOptions.HidesNormalPass || sh.Samplers.Count == 0,
            CastsShadow = !shapeOptions.HidesNormalPass && sh.RenderState.DepthWriteEnabled,
            Name = sh.Name,
            Material = sh.Material,
            Tags = shapeOptions.Tags,
            AlphaTest = sh.AlphaTest,
            Blend = sh.RenderState.Blend,
            RenderState = sh.RenderState,
            VertexBuffer = buffers.Vbo,
            IndexBuffer = buffers.Ibo,
            IndexCount = sh.IndexCount,
            Lods = sh.Lods is { Count: > 0 } lods
                ? [.. lods.Where(l => l.Length == 2).Select(l => (l[0], l[1]))]
                : [(0, sh.IndexCount)],
            VertexSkinCount = sh.VertexSkinCount,
            GBufferProgram = shapePrograms.GBuffer,
            GBufferSamplers = textures.Resolve(sh.Samplers),
            GBufferShaderName = sh.GBufferShader,
            // The z-only prepass has its own sampler layout, and many materials rely on it for the cutout their G-buffer program lacks.
            ZOnlyProgram = shapePrograms.ZOnly,
            ZOnlySamplers = SamplersFor(shapePrograms.ZOnly, sh.ZOnlySamplers),
            ZOnlyShaderName = shapePrograms.ZOnly != 0 ? sh.ZOnlyShader : "",
            // The forward program is built for any shape that has one, not only blended surfaces: opaque materials read gsys_material
            // fields no other program touches, so it must never be gated on RenderState.Blend.
            ForwardProgram = shapePrograms.Forward,
            ForwardSamplers = SamplersFor(shapePrograms.Forward, sh.MaterialSamplers),
            ForwardShaderName = shapePrograms.Forward != 0 ? sh.MaterialShader : "",
            ReadsSceneColor = gl.GetUniformLocation(shapePrograms.GBuffer, "cTex_ColorBuffer") >= 0,
            MaterialBlock = new MaterialBlock(gl, File.ReadAllBytes(Path.Combine(dataDirectory, sh.MaterialFile))),
            MaterialParams = MaterialParamLayout.TryLoadBeside(dataDirectory, sh.MaterialFile),
        };
    }

    IReadOnlyList<ShapeSampler> SamplersFor(uint program, IReadOnlyList<SamplerBinding> bindings) =>
        program != 0 ? textures.Resolve(bindings) : [];

    void CreateVertexArrays(LoadedShape shape, ShapeBuffers buffers)
    {
        shape.GBufferVao = _arrays.ForProgram(shape.GBufferProgram, buffers);
        if (shape.ZOnlyProgram != 0)
            shape.ZOnlyVao = _arrays.ForProgram(shape.ZOnlyProgram, buffers);
        if (shape.ForwardProgram != 0)
            shape.ForwardVao = _arrays.ForProgram(shape.ForwardProgram, buffers);
        shape.PassIdVao = _arrays.ForPassId(buffers);
    }
}
