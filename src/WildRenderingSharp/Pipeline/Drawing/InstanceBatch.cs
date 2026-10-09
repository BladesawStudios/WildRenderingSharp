using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Animation.Posing;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Baking;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>
/// Every placement of one model, drawn with instanced calls through the game's own shaders (see <c>InstancedShaderPatch</c> for how
/// they read it).
/// </summary>
public sealed class InstanceBatch : IDisposable
{
    internal const int Row8Offset = InstanceData.Row8Offset, PaletteOffset = InstanceData.PaletteOffset;

    readonly GL _gl;
    readonly Vector4[] _data;

    public InstanceBatch(GL gl, LoadedModel model, IReadOnlyList<Vector4[]> placements)
    {
        _gl = gl;
        Model = model;
        Count = placements.Count;
        Placements = placements;
        Shadow = new InstanceShadowRuns(placements, model.BoundsRadius);

        Matrix4x4[] bindPalette = SkeletonPose.BindPalette(model.Skeleton);
        PaletteRepeats = bindPalette.Length == 0;
        PaletteVec4s = (PaletteRepeats ? 1 : bindPalette.Length) * 3;
        Stride = PaletteOffset + PaletteVec4s;

        var packed = InstanceData.Pack(placements, model.BoundsMin, model.BoundsMax, bindPalette, Stride);
        (_data, BoundsMin, BoundsMax) = (packed.Vectors, packed.BoundsMin, packed.BoundsMax);
        Buffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, Buffer);
        gl.BufferData<Vector4>(BufferTargetARB.ShaderStorageBuffer, _data, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    public LoadedModel Model { get; }

    public int Count { get; }

    public int Stride { get; }

    public int PaletteVec4s { get; }

    public bool PaletteRepeats { get; }

    public Vector3 BoundsMin { get; }

    public Vector3 BoundsMax { get; }

    public IReadOnlyList<Vector4[]> Placements { get; }

    internal uint Buffer { get; }

    public List<(int First, int Count, int Lod)> Visible { get; } = [];

    public InstanceShadowRuns Shadow { get; }

    public bool IncludeBlended { get; set; } = true;

    public int[]? BakeAtlasOfInstance { get; private set; }

    public IReadOnlyList<LoadedTexture> BakeAtlases { get; private set; } = [];

    internal uint BakeTable { get; private set; }

    public void ShowAll(int lod = 0)
    {
        Visible.Clear();
        if (Count > 0)
            Visible.Add((0, Count, lod));
    }

    // Points each instance at its baked-lighting atlas and uploads the table its shaders look the atlas rectangles up in.
    public void SetBake(IReadOnlyList<BakeActor?> perInstance)
    {
        var bake = InstanceBakeTable.Build(Count, perInstance);
        WriteBakeRows(bake.Row8);

        if (BakeTable != 0)
            _gl.DeleteBuffer(BakeTable);
        BakeTable = 0;
        if (bake.Atlases.Count > 0)
        {
            BakeTable = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, BakeTable);
            _gl.BufferData<Vector4>(BufferTargetARB.ShaderStorageBuffer, bake.Table, BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        }
        BakeAtlasOfInstance = bake.Atlases.Count > 0 ? bake.AtlasOfInstance : null;
        BakeAtlases = bake.Atlases;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(Buffer);
        if (BakeTable != 0)
            _gl.DeleteBuffer(BakeTable);
    }

    // One upload, and only when a row changed: thousands of 16-byte writes into a buffer the GPU may still be reading would each stall on it.
    void WriteBakeRows(Vector4[] row8)
    {
        bool changed = false;
        for (int i = 0; i < Count; i++)
        {
            int at = i * Stride + Row8Offset;
            if (_data[at] == row8[i])
                continue;
            _data[at] = row8[i];
            changed = true;
        }
        if (!changed)
            return;
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, Buffer);
        _gl.BufferSubData<Vector4>(BufferTargetARB.ShaderStorageBuffer, 0, _data);
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }
}
