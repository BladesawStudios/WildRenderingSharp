using System.Numerics;
using System.Runtime.CompilerServices;
using WildRenderingSharp.Animation.Posing;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>Comparisons and bounds that decide whether a shadow map drawn earlier is still valid.</summary>
internal static class ShadowSignatures
{
    public static bool ActorRowsEqual(Vector4[][] a, Vector4[][] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!RowsEqual(a[i], b[i]))
                return false;
        return true;
    }

    static bool RowsEqual(Vector4[] a, Vector4[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    public static bool PosesEqual(Matrix4x4[]?[]? cached, IReadOnlyList<ActorRenderInput> actors)
    {
        if (cached is null || cached.Length != actors.Count)
            return false;
        for (int i = 0; i < cached.Length; i++)
        {
            var before = cached[i];
            var now = actors[i].BoneWorldMatrices;
            if (before is null || now is null)
            {
                if (before is not null || now is not null)
                    return false;
                continue;
            }
            if (!before.AsSpan().SequenceEqual(now))
                return false;
        }
        return true;
    }

    public static long InstanceSignature(IReadOnlyList<InstanceBatch> instances, bool shadowRuns)
    {
        var hash = new HashCode();
        foreach (var batch in instances)
        {
            hash.Add(RuntimeHelpers.GetHashCode(batch));
            foreach (var run in shadowRuns ? batch.Shadow.Visible : batch.Visible)
                hash.Add(run);
        }
        return hash.ToHashCode();
    }

    public static long ActorSignature(IReadOnlyList<ActorRenderInput> actors)
    {
        var hash = new HashCode();
        foreach (var actor in actors)
        {
            foreach (var row in actor.ModelMatrixRows)
                hash.Add(row);
            if (actor.BoneWorldMatrices is { } bones)
                foreach (var matrix in bones)
                    hash.Add(matrix);
        }
        return hash.ToHashCode();
    }

    public static (Vector3 Lo, Vector3 Hi) CombinedBounds(IReadOnlyList<ActorRenderInput> actors, IReadOnlyList<InstanceBatch> instances)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var actor in actors)
        {
            var (posedLo, posedHi) = PosedBounds(actor);
            var (actorLo, actorHi) = RotateBounds(posedLo, posedHi, actor.ModelMatrixRows);
            lo = Vector3.Min(lo, actorLo);
            hi = Vector3.Max(hi, actorHi);
        }
        foreach (var batch in instances.Where(b => b.Count > 0))
        {
            lo = Vector3.Min(lo, batch.BoundsMin);
            hi = Vector3.Max(hi, batch.BoundsMax);
        }
        return (lo, hi);
    }

    // The model's bounds are those of its bind pose, so an actor posed far from it (a ragdoll thrown across the floor) would leave its shadow outside the map fitted to them.
    // Every bone that has moved from its bind place adds a box around where it is now, as far each way as a quarter of the model's size, which is more than the geometry a bone carries reaches.
    static (Vector3 Lo, Vector3 Hi) PosedBounds(ActorRenderInput actor)
    {
        var (lo, hi) = (actor.Model.BoundsMin, actor.Model.BoundsMax);
        if (actor.BoneWorldMatrices is not { } posed || actor.Model.Skeleton is not { } skeleton)
            return (lo, hi);

        Matrix4x4[] bind = SkeletonPose.SharedBindPoseWorld(skeleton);
        float reach = (hi - lo).Length() * 0.25f;
        var (bindLo, bindHi) = (lo, hi);
        for (int i = 0; i < posed.Length && i < bind.Length; i++)
        {
            Vector3 now = posed[i].Translation;
            if (Vector3.Distance(now, bind[i].Translation) < reach * 0.1f)
                continue;
            lo = Vector3.Min(lo, now - new Vector3(reach));
            hi = Vector3.Max(hi, now + new Vector3(reach));
        }
        return (Vector3.Min(lo, bindLo), Vector3.Max(hi, bindHi));
    }

    static (Vector3 Lo, Vector3 Hi) RotateBounds(Vector3 lo, Vector3 hi, ReadOnlySpan<Vector4> modelRows)
    {
        var model = GpuMatrix.FromRows(modelRows);
        var rotatedLo = new Vector3(float.MaxValue);
        var rotatedHi = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? lo.X : hi.X,
                (i & 2) == 0 ? lo.Y : hi.Y,
                (i & 4) == 0 ? lo.Z : hi.Z);
            var transformed = Vector3.Transform(corner, model);
            rotatedLo = Vector3.Min(rotatedLo, transformed);
            rotatedHi = Vector3.Max(rotatedHi, transformed);
        }
        return (rotatedLo, rotatedHi);
    }
}
