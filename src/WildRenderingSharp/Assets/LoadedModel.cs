using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Assets;

/// <summary>
/// A model loaded and ready to draw - the manifest plus every shape's GL objects and world-space bounds. Needs the GL context current; a
/// model loaded on a worker thread needs <see cref="FinishOnRenderThread"/> on the renderer's thread before drawing.
/// </summary>
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

    // Run once the shapes' vertex arrays exist, which is when they can be given up for the merged draw's copy.
    internal Action? AfterVertexArrays { get; set; }

    public void FinishOnRenderThread()
    {
        if (PendingVertexArrays is { } pending)
        {
            PendingVertexArrays = null;
            foreach (var build in pending)
                build();
        }
        if (AfterVertexArrays is { } after)
        {
            AfterVertexArrays = null;
            after();
        }
    }

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
            shape.Merged?.Release(shape);
            shape.DeleteGl(_gl);
            shape.MaterialBlock.Dispose();
        }
        Textures.Dispose();
    }
}
