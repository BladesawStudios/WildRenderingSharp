using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Sky.Clouds;

/// <summary>
/// WildRenderingSharp's OWN distance fade for the cloud dome, injected into the real <c>agl_cloud</c> shader as a small GLSL patch.
/// </summary>
internal static class CloudDistanceFade
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

}
