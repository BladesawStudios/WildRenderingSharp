using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// WildRenderingSharp's OWN distance fade for the cloud dome, injected into the real <c>agl_cloud</c> shader as a small GLSL patch.
/// </summary>
public static class CloudDistanceFade
{
    public const uint Binding = 27;

    static readonly string BlockDecl = GlslFiles.Load("Totk/Sky/CloudDistanceFade/BlockDecl.glsl");

    public static string PatchVertex(string source)
    {
        if (source.Contains("mrw_inner_main"))
            return source;
        source = source.Replace("void main()", "void mrw_inner_main()");
        return source + GlslFiles.Load("Totk/Sky/CloudDistanceFade/VertexEpilogue.glsl");
    }

    public static string PatchFragment(string source)
    {
        if (source.Contains("mrw_inner_main"))
            return source;
        source = source.Replace("void main()", "void mrw_inner_main()");
        return source + "\n\n" + BlockDecl + GlslFiles.Load("Totk/Sky/CloudDistanceFade/FragmentEpilogue.glsl");
    }

    public static byte[] BuildUbo(float startDistance, float ramp, bool exponential, float strength,
        Vector3 extents, Vector3 skyColor)
    {
        var buf = new byte[48];
        var u = new UniformWriter(buf);
        u.Set(0, 0, MathF.Max(0f, startDistance));
        u.Set(0, 1, MathF.Max(0f, ramp));
        u.Set(0, 2, exponential ? 1f : 0f);
        u.Set(0, 3, strength);
        u.Set(1, 0, extents.X); u.Set(1, 1, extents.Y); u.Set(1, 2, extents.Z);
        u.Set(2, 0, skyColor.X); u.Set(2, 1, skyColor.Y); u.Set(2, 2, skyColor.Z);
        return buf;
    }
}
