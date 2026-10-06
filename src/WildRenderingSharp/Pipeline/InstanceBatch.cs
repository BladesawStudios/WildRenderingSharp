using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Every placement of one model, drawn with instanced calls through the game's own shaders - see
/// <see cref="InstancedShaderPatch"/> for how the shaders read it.
/// </summary>
/// <remarks>
/// <para>
/// Built for static placements in bind pose - a map's trees, rocks and buildings. Each instance
/// carries exactly the two blocks the per-actor path would bind for it: its <c>ShpMtx</c> rows (its
/// placement) and its <c>_Mtx</c> bone palette (the model's bind-pose palette with the placement
/// folded in, as <see cref="Shaders.Profiles.Totk.Ubos.BonePaletteUbo.Build"/> does it), back to back
/// in one storage buffer.
/// </para>
/// <para>
/// The host decides what is drawn: <see cref="Visible"/> holds runs of instances and the level of
/// detail each run draws at, refilled as often as the host culls. A batch drawn with nothing
/// visible draws nothing, and costs nothing.
/// </para>
/// </remarks>
public sealed class InstanceBatch : IDisposable
{
    readonly GL _gl;

    public LoadedModel Model { get; }

    /// <summary>How many placements the buffer holds.</summary>
    public int Count { get; }

    /// <summary>vec4s per instance: three ShpMtx rows, then three per palette slot.</summary>
    public int Stride { get; }

    /// <summary>vec4s of bone palette per instance.</summary>
    public int PaletteVec4s { get; }

    /// <summary>True for a model with no skeleton, whose palette is its placement in every slot.</summary>
    public bool PaletteRepeats { get; }

    /// <summary>The union of every placement's box, world space.</summary>
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }

    /// <summary>The placements' own rows, as given - kept for picking and bounds.</summary>
    public IReadOnlyList<Vector4[]> Placements { get; }

    internal uint Buffer { get; }

    /// <summary>
    /// Runs of instances to draw this frame, and the level of detail each draws at - a level past a
    /// shape's own chain draws its coarsest (<see cref="LoadedShape.Lod"/>).
    /// </summary>
    public List<(int First, int Count, int Lod)> Visible { get; } = [];

    /// <summary>
    /// Whether the model's blended (see-through) shapes draw too. A host compositing this frame
    /// under its own see-through layers - water, say - turns it off and draws those itself.
    /// </summary>
    public bool IncludeBlended { get; set; } = true;

    /// <summary>
    /// Runs of instances inside the shadow focus, which is what the shadow map draws when a frame
    /// has one (<see cref="FrameRequest.ShadowFocus"/>) - chosen by the shadow region, not by what
    /// the camera sees, so turning the view neither redraws the map nor drops a caster just
    /// off-screen. Recomputed only when the focus moves.
    /// </summary>
    public List<(int First, int Count, int Lod)> ShadowVisible { get; } = [];

    ShadowFocus? _shadowFocus;

    /// <summary>The level of detail shadow casters draw at: shapes, not surface detail, decide a shadow.</summary>
    const int ShadowLod = 1;

    /// <summary>Fills <see cref="ShadowVisible"/> for <paramref name="focus"/>, if it changed.</summary>
    internal void UpdateShadowRuns(ShadowFocus focus)
    {
        if (_shadowFocus == focus)
            return;
        _shadowFocus = focus;
        ShadowVisible.Clear();

        float reach = focus.Radius + Model.BoundsRadius;
        int start = -1;
        for (int i = 0; i <= Count; i++)
        {
            bool inside = false;
            if (i < Count)
            {
                Vector4[] r = Placements[i];
                var position = new Vector3(r[0].W, r[1].W, r[2].W);
                Vector3 d = Vector3.Abs(position - focus.Center);
                inside = d.X <= reach && d.Y <= reach && d.Z <= reach;
            }
            if (inside && start < 0)
                start = i;
            else if (!inside && start >= 0)
            {
                ShadowVisible.Add((start, i - start, ShadowLod));
                start = -1;
            }
        }
    }

    /// <param name="placements">Each placement's model rows, the convention <see cref="ActorRenderInput.ModelMatrixRows"/> uses.</param>
    public InstanceBatch(GL gl, LoadedModel model, IReadOnlyList<Vector4[]> placements)
    {
        _gl = gl;
        Model = model;
        Count = placements.Count;
        Placements = placements;

        Matrix4x4[] local = BindPalette(model.Skeleton);
        PaletteRepeats = local.Length == 0;
        int slots = PaletteRepeats ? 1 : local.Length;
        PaletteVec4s = slots * 3;
        Stride = 3 + PaletteVec4s;

        var data = new Vector4[Math.Max(1, Count) * Stride];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (int i = 0; i < Count; i++)
        {
            Vector4[] rows = placements[i];
            int at = i * Stride;
            data[at] = rows[0];
            data[at + 1] = rows[1];
            data[at + 2] = rows[2];

            if (PaletteRepeats)
            {
                data[at + 3] = rows[0];
                data[at + 4] = rows[1];
                data[at + 5] = rows[2];
            }
            else
            {
                Matrix4x4 placement = MatrixFromRows(rows);
                for (int s = 0; s < local.Length; s++)
                {
                    Matrix4x4 m = local[s] * placement;
                    int p = at + 3 + s * 3;
                    data[p] = new Vector4(m.M11, m.M21, m.M31, m.M41);
                    data[p + 1] = new Vector4(m.M12, m.M22, m.M32, m.M42);
                    data[p + 2] = new Vector4(m.M13, m.M23, m.M33, m.M43);
                }
            }

            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) == 0 ? model.BoundsMin.X : model.BoundsMax.X,
                    (c & 2) == 0 ? model.BoundsMin.Y : model.BoundsMax.Y,
                    (c & 4) == 0 ? model.BoundsMin.Z : model.BoundsMax.Z);
                var w = new Vector3(
                    Vector4.Dot(rows[0], new Vector4(corner, 1)),
                    Vector4.Dot(rows[1], new Vector4(corner, 1)),
                    Vector4.Dot(rows[2], new Vector4(corner, 1)));
                lo = Vector3.Min(lo, w);
                hi = Vector3.Max(hi, w);
            }
        }
        BoundsMin = Count > 0 ? lo : Vector3.Zero;
        BoundsMax = Count > 0 ? hi : Vector3.Zero;

        Buffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, Buffer);
        gl.BufferData<Vector4>(BufferTargetARB.ShaderStorageBuffer, data, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    /// <summary>Draws every visible run.</summary>
    public void ShowAll(int lod = 0)
    {
        Visible.Clear();
        if (Count > 0)
            Visible.Add((0, Count, lod));
    }

    /// <summary>
    /// The model's bind-pose palette with no placement in it: inverse-bind times bone for the smooth
    /// slots, the bone alone for the rigid ones - BonePaletteUbo.Build's own two loops. Empty for a
    /// model with no skeleton.
    /// </summary>
    internal static Matrix4x4[] BindPalette(SkeletonManifest? skeleton)
    {
        if (skeleton is not { } skel)
            return [];
        Matrix4x4[] boneWorld = SkeletonPose.BindPoseWorldMatrices(skel);
        ReadOnlySpan<int> matrixToBone = CollectionsMarshal.AsSpan(skel.MatrixToBoneList);
        Matrix4x4[] inverse = skel.InverseModelMatricesAsMatrices();
        var palette = new Matrix4x4[matrixToBone.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            int bone = matrixToBone[i];
            bool known = bone >= 0 && bone < boneWorld.Length;
            palette[i] = i < inverse.Length
                ? (known ? inverse[i] * boneWorld[bone] : Matrix4x4.Identity)
                : (known ? boneWorld[bone] : Matrix4x4.Identity);
        }
        return palette;
    }

    /// <summary>The rows convention (translation in each row's W) as a row-vector Matrix4x4 - the transpose DeferredPipeline.BuildBonePalette applies.</summary>
    static Matrix4x4 MatrixFromRows(Vector4[] r) => new(
        r[0].X, r[1].X, r[2].X, 0,
        r[0].Y, r[1].Y, r[2].Y, 0,
        r[0].Z, r[1].Z, r[2].Z, 0,
        r[0].W, r[1].W, r[2].W, 1);

    public void Dispose() => _gl.DeleteBuffer(Buffer);
}
