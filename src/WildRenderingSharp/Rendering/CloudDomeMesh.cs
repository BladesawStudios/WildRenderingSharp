using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Generates the real <c>agl::fx::Cloud</c> dome mesh procedurally - traced from the real
/// <c>agl::fx::Cloud::initVertex_</c> decompile (Ghidra), not an invented approximation. The real
/// game never loads a mesh file for this: it builds a local-space unit dome in code, 24 vertices
/// per ring (a fixed constant - confirmed as the real angle step read straight from the game's own
/// data segment, 0.2617994 rad = 2*pi/24 = 15 degrees) around a caller-supplied ring count (the
/// real game builds two LOD instances from the same code, one with 12 rings and one with 24 - see
/// the two <c>initVertex_</c> call sites in <c>Cloud::initialize</c>), plus one apex vertex that
/// closes the top.
///
/// Per ring <c>i</c> (0 = outermost/rim ring, working inward):
/// <code>
/// t = i / rings
/// radius = 1 - t^3                          // 1 at the rim, tapering toward 0 near the apex
/// circleHeight = sqrt(1 - radius^2)         // standard unit-circle height for that radius
/// height = circleHeight &lt;= 0.1
///     ? (0.1 - circleHeight) * -0.3 + 0.1   // flattens the dome near the rim/horizon
///     : circleHeight
/// y = height - 0.07                         // shifts the rim exactly to y=0
/// vertex(radius*sin(angle), y, radius*cos(angle))  for angle = j * (2*pi/24), j = 0..23
/// </code>
/// then one final apex vertex at <c>(0, 1, 0)</c>.
///
/// The four magic constants (-0.07, 0.1, -0.3, 2*pi/24) are the real game's own values, read
/// directly out of the game binary's data segment via Ghidra - not tuned or guessed. The
/// rim-flattening branch is a real, deliberate design choice: without it the dome would be a plain
/// hemisphere with a hard vertical wall at the horizon; the real game instead ramps the last ~10%
/// of height smoothly, giving a shallow "false horizon" the sky/cloud blend can fade into.
///
/// Winding/index order was NOT traced (<c>Cloud::initIndex_</c> wasn't read, only
/// <c>initVertex_</c>) - <see cref="BuildIndices"/> assumes counter-clockwise; flip it if backfaces
/// show up.
/// </summary>
public static class CloudDomeMesh
{
    public const int SegmentsPerRing = 24;

    /// <summary>Real ring counts for the game's two LOD dome instances (see <c>Cloud::initialize</c>'s two <c>initVertex_</c> calls, args 0xc and 0x18).</summary>
    public const int NearRings = 12;
    public const int FarRings = 24;

    /// <summary>Builds the real dome's vertex positions in local unit-dome space (scale/translate to taste - the real game applies <c>mSkyScale</c>/<c>mSkyHeight</c> from <see cref="CloudPostFxLayer"/>) for a given ring count.</summary>
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

    /// <summary>Triangle indices connecting consecutive rings plus a fan closing the innermost ring to the apex vertex. See the class remarks for the winding-order caveat.</summary>
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
