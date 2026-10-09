using System.Numerics;
using System.Runtime.CompilerServices;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>Comparisons and bounds that decide whether a shadow map drawn earlier is still valid.</summary>
public static class ShadowSignatures
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
            foreach (var run in shadowRuns ? batch.ShadowVisible : batch.Visible)
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
            var (actorLo, actorHi) = RotateBounds(actor.Model.BoundsMin, actor.Model.BoundsMax, actor.ModelMatrixRows);
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

    static (Vector3 Lo, Vector3 Hi) RotateBounds(Vector3 lo, Vector3 hi, ReadOnlySpan<Vector4> modelRows)
    {
        var model = CameraData.FromRows(modelRows);
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
