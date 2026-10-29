using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// The cleaned vertex and fragment sources of a program the preparer extracted from the game.
/// </summary>
/// <remarks>
/// A program is two files in the shaders folder, named for it with <c>_extracted</c> before the stage's extension. The fragment stage also has its unwritten outputs
/// declared, as every extracted fragment stage does before it is linked.
/// </remarks>
internal static class ExtractedProgramSource
{
    public static (string Vertex, string Fragment) Read(string shadersDir, string name) =>
        (TotkGlsl.Clean(File.ReadAllText(Path.Combine(shadersDir, name + "_extracted.vert"))),
         UnwrittenOutputs.Apply(TotkGlsl.Clean(File.ReadAllText(Path.Combine(shadersDir, name + "_extracted.frag")))));
}
