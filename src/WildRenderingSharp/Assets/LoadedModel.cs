using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>A model loaded and ready to draw - the manifest plus every shape's GL objects and world-space bounds.</summary>
public sealed class LoadedModel : IDisposable
{
    readonly GL _gl;

    public required ModelManifest Manifest { get; init; }
    public required IReadOnlyList<LoadedShape> Shapes { get; init; }
    public required Vector3 BoundsMin { get; init; }
    public required Vector3 BoundsMax { get; init; }

    /// <summary>Null for a model with no skeleton at all (e.g. a static prop) - <see cref="Pipeline.DeferredPipeline"/> falls back to <c>BonePaletteUbo.FillIdentity</c> in that case.</summary>
    public SkeletonManifest? Skeleton { get; init; }

    /// <summary>
    /// The prepared model's cache directory. Besides what the renderer loads, the preparer leaves the
    /// actor's Havok Cloth (<c>*.bphcl</c>) and Phive Helper Bone (<c>*.bphhb</c>) files here, for a
    /// host that simulates them (the renderer does not - see <see cref="Scene.RenderActor.ModifyPose"/>).
    /// </summary>
    public required string DataDirectory { get; init; }

    /// <summary>Every embedded anim's name (<c>&lt;modelName&gt;.&lt;AnimName&gt;.anim.json</c>) - load the full curve data with <see cref="SkeletalAnimManifest.Load"/> only once one is actually selected to play.</summary>
    public IReadOnlyList<string> AvailableAnims { get; init; } = [];

    /// <summary>Every texture pattern anim's name (<c>&lt;modelName&gt;.&lt;AnimName&gt;.texpat.json</c>) - a separate list from <see cref="AvailableAnims"/> because the two are independent: a pattern anim swaps a material's textures and moves nothing, so one can play with or without a skeletal anim.</summary>
    public IReadOnlyList<string> AvailableTexturePatternAnims { get; init; } = [];

    /// <summary>Every shader parameter anim's name (<c>&lt;modelName&gt;.&lt;AnimName&gt;.matanim.json</c>) - covering BFRES's <c>_fsp</c>, <c>_fcl</c> and <c>_fts</c> alike, since all three write shader parameters. Load one with <see cref="MaterialAnimManifest.Load"/> to find out which kind it is.</summary>
    public IReadOnlyList<string> AvailableMaterialAnims { get; init; } = [];

    /// <summary>This model's texture cache - exposed so a texture pattern anim can pull in an alternate texture on demand (see <see cref="WildRenderingSharp.Rendering.TexturePatternPose"/>) and have it cached like any other.</summary>
    public TextureCache Textures { get; }

    /// <summary>Every vertex position across every shape - used for icon-capture's rotated-silhouette camera fit (see <c>IconCapturePreset.Frame</c>), which needs the real mesh shape, not just its axis-aligned box.</summary>
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
            _gl.DeleteBuffer(shape.MaterialUboBuffer);
        }
        Textures.Dispose();
    }
}
