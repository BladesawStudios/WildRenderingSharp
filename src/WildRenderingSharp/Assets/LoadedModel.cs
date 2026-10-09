using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Assets;

/// <summary>A model loaded and ready to draw - the manifest plus every shape's GL objects and world-space bounds. Needs the GL context current; a model loaded on a worker thread needs <see cref="FinishOnRenderThread"/> on the renderer's thread before drawing.</summary>
public sealed class LoadedModel : IDisposable
{
    readonly GL _gl;

    internal ModelManifest Manifest { get; init; } = null!;
    public required IReadOnlyList<LoadedShape> Shapes { get; init; }
    public required Vector3 BoundsMin { get; init; }
    public required Vector3 BoundsMax { get; init; }

    public SkeletonManifest? Skeleton { get; init; }

    public required string DataDirectory { get; init; }

    public IReadOnlyList<string> AvailableAnims { get; init; } = [];

    public IReadOnlyList<string> AvailableTexturePatternAnims { get; init; } = [];

    public IReadOnlyList<string> AvailableMaterialAnims { get; init; } = [];

    internal TextureCache Textures { get; }

    internal List<Action>? PendingVertexArrays { get; set; }

    public void FinishOnRenderThread()
    {
        if (PendingVertexArrays is not { } pending)
            return;
        PendingVertexArrays = null;
        foreach (var build in pending)
            build();
    }

    public required IReadOnlyList<Vector3> VertexPositions { get; init; }

    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;
    public float BoundsRadius => (BoundsMax - BoundsMin).Length() * 0.5f;

    internal LoadedModel(GL gl, TextureCache textures)
    {
        _gl = gl;
        Textures = textures;
    }

    public void Dispose()
    {
        foreach (var shape in Shapes)
        {
            _gl.DeleteVertexArray(shape.GBufferVao);
            if (shape.ZOnlyVao != 0) _gl.DeleteVertexArray(shape.ZOnlyVao);
            if (shape.ForwardVao != 0) _gl.DeleteVertexArray(shape.ForwardVao);
            _gl.DeleteVertexArray(shape.PassIdVao);
            _gl.DeleteBuffer(shape.VertexBuffer);
            _gl.DeleteBuffer(shape.IndexBuffer);
            shape.MaterialBlock.Dispose();
        }
        Textures.Dispose();
    }
}
