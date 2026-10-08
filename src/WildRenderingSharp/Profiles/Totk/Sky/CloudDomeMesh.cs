using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Generates the real <c>agl::fx::Cloud</c> dome mesh procedurally - traced from the real <c>agl::fx::Cloud::initVertex_</c>
/// decompile (Ghidra), not an invented approximation.
/// </summary>
public static class CloudDomeMesh
{
    public const int SegmentsPerRing = 24;

    public const int NearRings = 12;
    public const int FarRings = 24;

    public static Vector3[] BuildVertices(int rings)
    {
        var verts = new Vector3[rings * SegmentsPerRing + 1];
        int idx = 0;
        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / rings;
            float radius = 1f - t * t * t;
            float circleHeight = MathF.Sqrt(MathF.Max(0f, 1f - radius * radius));
            float height = circleHeight <= 0.1f ? (0.1f - circleHeight) * -0.3f + 0.1f : circleHeight;
            float y = height - 0.07f;

            for (int j = 0; j < SegmentsPerRing; j++)
            {
                float angle = j * (MathF.PI * 2f / SegmentsPerRing);
                verts[idx++] = new Vector3(radius * MathF.Sin(angle), y, radius * MathF.Cos(angle));
            }
        }
        verts[idx] = new Vector3(0f, 1f, 0f);
        return verts;
    }

    public static int[] BuildIndices(int rings)
    {
        var idx = new List<int>();
        int apex = rings * SegmentsPerRing;
        for (int ring = 0; ring < rings - 1; ring++)
        {
            int a0 = ring * SegmentsPerRing;
            int a1 = (ring + 1) * SegmentsPerRing;
            for (int j = 0; j < SegmentsPerRing; j++)
            {
                int jNext = (j + 1) % SegmentsPerRing;
                idx.Add(a0 + j); idx.Add(a1 + j); idx.Add(a0 + jNext);
                idx.Add(a0 + jNext); idx.Add(a1 + j); idx.Add(a1 + jNext);
            }
        }

        int last = (rings - 1) * SegmentsPerRing;
        for (int j = 0; j < SegmentsPerRing; j++)
        {
            int jNext = (j + 1) % SegmentsPerRing;
            idx.Add(last + j); idx.Add(apex); idx.Add(last + jNext);
        }
        return idx.ToArray();
    }
}
