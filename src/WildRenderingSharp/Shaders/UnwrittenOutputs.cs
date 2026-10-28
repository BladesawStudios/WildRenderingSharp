using System.Text;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Shaders;

/// <summary>Makes a G-buffer program write every layer of the G-buffer, so a layer it has nothing to say about is left cleared and not as the shape behind it left it.</summary>
/// <remarks>
/// The game's material programs write only some of the G-buffer's layers: the albedo and normal always, and the emission and a material ID in some. The layers a program does not write
/// keep whatever an earlier draw put on those pixels, and draws are ordered by program and not by depth, so a shape behind another can leave its material ID and emission under
/// the shape in front, which then shows in everything read from them. The fill has an alpha of zero, so a draw that blends keeps what is under it, as it did before.
/// </remarks>
internal static partial class UnwrittenOutputs
{
    // The G-buffer's layers, by the location a program writes each at.
    const int Layers = 6;

    [GeneratedRegex(@"layout\s*\(\s*location\s*=\s*(\d+)\s*\)\s*out\s+vec4\s+(\w+)")]
    private static partial Regex OutputDeclaration();

    public static string Apply(string fragment)
    {
        var written = new HashSet<int>();
        bool material = false;
        foreach (Match m in OutputDeclaration().Matches(fragment))
        {
            int location = int.Parse(m.Groups[1].Value);
            written.Add(location);
            material |= m.Groups[2].Value.StartsWith("out_attr", StringComparison.Ordinal) && location is 1 or 3;
        }

        // Only a material program of the G-buffer writes the albedo and the normal; a depth-only or screen program is left as it is.
        if (!material || !written.Contains(1) || !written.Contains(3))
            return fragment;

        var declarations = new StringBuilder();
        var assignments = new StringBuilder();
        for (int location = 0; location < Layers; location++)
        {
            if (written.Contains(location))
                continue;
            declarations.Append($"layout (location = {location}) out vec4 wrs_fill{location};\n");
            assignments.Append($"    wrs_fill{location} = vec4(0.0);\n");
        }

        int end = fragment.LastIndexOf('}');
        int main = fragment.IndexOf("void main()", StringComparison.Ordinal);
        if (end < 0 || main < 0 || assignments.Length == 0)
            return fragment;

        return fragment[..main] + declarations + fragment[main..end] + assignments + fragment[end..];
    }
}
