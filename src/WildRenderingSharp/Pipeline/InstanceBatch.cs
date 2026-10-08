using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Every placement of one model, drawn with instanced calls through the game's own shaders (see <c>InstancedShaderPatch</c> for how
/// they read it).
/// </summary>
/// <remarks>
/// <para>
/// Built for static placements in bind pose: a map's trees, rocks and buildings. Each instance carries the two blocks the
/// per-actor path would bind for it, its <c>ShpMtx</c> rows (placement, then row 8; see <see cref="SetBake"/>) and its
/// <c>_Mtx</c> bone palette (the bind-pose palette with the placement folded in, as <c>BonePaletteUbo.Build</c> does), back
/// to back in one storage buffer.
/// </para>
/// <para>
/// The host decides what is drawn: <see cref="Visible"/> holds runs of instances and the level of detail of each, refilled
/// as often as the host culls. A batch with nothing visible draws nothing.
/// </para>
/// </remarks>
public sealed class InstanceBatch : IDisposable
{
    readonly GL _gl;

    public LoadedModel Model { get; }

    /// <summary>How many placements the buffer holds.</summary>
    public int Count { get; }

    /// <summary>vec4s per instance: three ShpMtx rows, ShpMtx row 8, then three per palette slot.</summary>
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

    readonly Vector4[] _data;

    /// <summary>
    /// Runs of instances to draw this frame, and the level of detail each draws at - a level past a shape's own chain draws its
    /// coarsest (<see cref="LoadedShape.Lod"/>).
    /// </summary>
    public List<(int First, int Count, int Lod)> Visible { get; } = [];

    /// <summary>
    /// Whether the model's blended (see-through) shapes draw too. A host compositing this frame under its own see-through layers -
    /// water, say - turns it off and draws those itself.
    /// </summary>
    public bool IncludeBlended { get; set; } = true;

    /// <summary>
    /// Runs of instances inside the shadow focus, which the shadow map draws when a frame has one (<see
    /// cref="FrameRequest.ShadowFocus"/>). Chosen by the shadow region, not the camera, so turning the view neither redraws the map
    /// nor drops a caster just off-screen.
    /// </summary>
    public List<(int First, int Count, int Lod)> ShadowVisible { get; } = [];

    ShadowFocus? _shadowFocus;

    // The level of detail shadow casters draw at: the finest. A caster also receives its own shadow, and a coarser level is a
    // different surface that stands proud in places and shadows it in hard-edged patches.
    const int ShadowLod = 0;

    internal void UpdateShadowRuns(ShadowFocus focus)
    {
        if (_shadowFocus == focus)
            return;
        _shadowFocus = focus;
        FillRuns(ShadowVisible, focus, ShadowLod);
    }

    readonly List<(int First, int Count, int Lod)>?[] _cascadeRuns = new List<(int, int, int)>?[RenderTargets.MaxCascades];
    readonly (ShadowFocus Focus, Vector3 Right, Vector3 Up)?[] _cascadeFocus = new (ShadowFocus, Vector3, Vector3)?[RenderTargets.MaxCascades];

    /// <summary>
    /// Which shadow cascades these instances cast into, a bit per cascade; all by default. A host swapping a model for a cruder
    /// stand-in by distance (a landmark's <c>_Far</c> model) never draws both, but casters are chosen by region, so both cast and
    /// the stand-in's shell shadowed the real model in patches.
    /// </summary>
    public int ShadowCascadeMask
    {
        get => _shadowCascadeMask;
        set
        {
            if (_shadowCascadeMask == value)
                return;
            _shadowCascadeMask = value;
            Array.Clear(_cascadeFocus);
        }
    }

    int _shadowCascadeMask = ~0;

    internal List<(int First, int Count, int Lod)> CascadeRuns(int cascade) => _cascadeRuns[cascade] ??= [];

    // Fills a cascade's runs if its region or the sun moved: every instance whose placement falls inside the square the
    // cascade's light projection covers, halfExtent along the light's right and up axes about the region's centre, at any depth
    // along the sun. Choosing by the region's own box left out casters standing outside it whose shadow falls inside, an
    // unshadowed band at each cascade's edge. Far cascades draw coarser levels of detail.
    internal void UpdateCascadeRuns(int cascade, ShadowFocus focus, Vector3 right, Vector3 up, float halfExtent)
    {
        if (_cascadeFocus[cascade] == (focus, right, up) && _cascadeRuns[cascade] is not null)
            return;
        _cascadeFocus[cascade] = (focus, right, up);
        var runs = CascadeRuns(cascade);
        runs.Clear();
        if ((_shadowCascadeMask & (1 << cascade)) == 0)
            return;
        float reach = halfExtent + Model.BoundsRadius;
        // The near two cascades at full detail (see ShadowLod); past them the view draws coarse levels too.
        int lod = Math.Max(ShadowLod, cascade - 1);
        int start = -1;
        for (int i = 0; i <= Count; i++)
        {
            bool inside = false;
            if (i < Count)
            {
                Vector4[] r = Placements[i];
                var d = new Vector3(r[0].W, r[1].W, r[2].W) - focus.Center;
                inside = MathF.Abs(Vector3.Dot(d, right)) <= reach && MathF.Abs(Vector3.Dot(d, up)) <= reach;
            }
            if (inside && start < 0)
                start = i;
            else if (!inside && start >= 0)
            {
                runs.Add((start, i - start, lod));
                start = -1;
            }
        }
    }

    void FillRuns(List<(int First, int Count, int Lod)> runs, ShadowFocus focus, int lod)
    {
        runs.Clear();
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
                runs.Add((start, i - start, lod));
                start = -1;
            }
        }
    }

    public InstanceBatch(GL gl, LoadedModel model, IReadOnlyList<Vector4[]> placements, IWorldBasis world)
    {
        _gl = gl;
        Model = model;
        Count = placements.Count;
        Placements = placements;

        Matrix4x4[] local = BindPalette(model.Skeleton);
        PaletteRepeats = local.Length == 0;
        int slots = PaletteRepeats ? 1 : local.Length;
        PaletteVec4s = slots * 3;
        Stride = PaletteOffset + PaletteVec4s;

        var data = new Vector4[Math.Max(1, Count) * Stride];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (int i = 0; i < Count; i++)
        {
            // The programs read the game's world (the profile's basis); the placements stay as given, for bounds, culling and shadows.
            Vector4[] rows = world.PlacementRows(placements[i]);
            Vector4[] given = placements[i];
            int at = i * Stride;
            data[at] = rows[0];
            data[at + 1] = rows[1];
            data[at + 2] = rows[2];

            if (PaletteRepeats)
            {
                data[at + PaletteOffset] = rows[0];
                data[at + PaletteOffset + 1] = rows[1];
                data[at + PaletteOffset + 2] = rows[2];
            }
            else
            {
                Matrix4x4 placement = MatrixFromRows(rows);
                for (int s = 0; s < local.Length; s++)
                {
                    Matrix4x4 m = local[s] * placement;
                    int p = at + PaletteOffset + s * 3;
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
                    Vector4.Dot(given[0], new Vector4(corner, 1)),
                    Vector4.Dot(given[1], new Vector4(corner, 1)),
                    Vector4.Dot(given[2], new Vector4(corner, 1)));
                lo = Vector3.Min(lo, w);
                hi = Vector3.Max(hi, w);
            }
        }
        BoundsMin = Count > 0 ? lo : Vector3.Zero;
        BoundsMax = Count > 0 ? hi : Vector3.Zero;

        _data = data;
        Buffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, Buffer);
        gl.BufferData<Vector4>(BufferTargetARB.ShaderStorageBuffer, data, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    internal const int Row8Offset = 3, PaletteOffset = 4;

    /// <summary>
    /// The bake atlas each instance samples, as an index into <see cref="BakeAtlases"/>; -1 for none (the material's own
    /// <c>bake0</c>).
    /// </summary>
    public int[]? BakeAtlasOfInstance { get; private set; }

    /// <summary>The distinct bake atlases this batch's instances use.</summary>
    public IReadOnlyList<LoadedTexture> BakeAtlases { get; private set; } = [];

    internal uint BakeTable { get; private set; }

    // Entries left empty at the head of the bake table: binding 0 is also where a water program writes a per-pixel value (entry
    // 28), so the table starts past it.
    const int BakeTableHead = 64;

    /// <summary>
    /// Gives each instance its baked lighting, as the game does: static-object shaders read <c>ShpMtx</c> row 8, and when its top
    /// two bits are <c>01</c> they take the bake texcoord scale and offset from a storage buffer at binding 0, entry <c>(row8.y
    /// &amp; 0xFFFFF) + gsys_material_id</c> (16 bytes each), instead of the material's <c>gsys_bake_st0</c>, then sample
    /// <c>bake0</c> there. So each baked instance gets a run of entries, one per material index, with its base written into row 8;
    /// the caller sets each shape's <c>gsys_material_id</c> to its material index and the draw binds the instance's atlas to
    /// <c>bake0</c>.
    /// </summary>
    public void SetBake(IReadOnlyList<BakeActor?> perInstance)
    {
        var atlases = new List<LoadedTexture>();
        var atlasOf = new int[Count];
        var table = new List<Vector4>(new Vector4[BakeTableHead]);
        bool changed = false;
        for (int i = 0; i < Count; i++)
        {
            atlasOf[i] = -1;
            var row8 = Vector4.Zero;
            if (i < perInstance.Count && perInstance[i] is { } bake && bake.StByMaterial.Length > 0)
            {
                int atlas = atlases.IndexOf(bake.Atlas);
                if (atlas < 0)
                {
                    atlas = atlases.Count;
                    atlases.Add(bake.Atlas);
                }
                atlasOf[i] = atlas;
                row8.Y = BitConverter.Int32BitsToSingle(0x40000000 | (table.Count & 0xFFFFF));
                table.AddRange(bake.StByMaterial);
            }
            int at = i * Stride + Row8Offset;
            if (_data[at] != row8)
            {
                _data[at] = row8;
                changed = true;
            }
        }
        // One upload: thousands of 16-byte writes into a buffer the GPU may still be reading would each stall on it.
        if (changed)
        {
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, Buffer);
            _gl.BufferSubData<Vector4>(BufferTargetARB.ShaderStorageBuffer, 0, _data);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        }

        if (BakeTable != 0)
            _gl.DeleteBuffer(BakeTable);
        BakeTable = 0;
        if (atlases.Count > 0)
        {
            BakeTable = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, BakeTable);
            _gl.BufferData<Vector4>(BufferTargetARB.ShaderStorageBuffer, table.ToArray(), BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        }
        BakeAtlasOfInstance = atlases.Count > 0 ? atlasOf : null;
        BakeAtlases = atlases;
    }

    /// <summary>Draws every visible run.</summary>
    public void ShowAll(int lod = 0)
    {
        Visible.Clear();
        if (Count > 0)
            Visible.Add((0, Count, lod));
    }

    // The model's bind-pose palette without a placement: inverse-bind times bone for smooth slots, the bone alone for rigid
    // ones. Empty for a model with no skeleton.
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

    static Matrix4x4 MatrixFromRows(Vector4[] r) => new(
        r[0].X, r[1].X, r[2].X, 0,
        r[0].Y, r[1].Y, r[2].Y, 0,
        r[0].Z, r[1].Z, r[2].Z, 0,
        r[0].W, r[1].W, r[2].W, 1);

    public void Dispose()
    {
        _gl.DeleteBuffer(Buffer);
        if (BakeTable != 0)
            _gl.DeleteBuffer(BakeTable);
    }
}
