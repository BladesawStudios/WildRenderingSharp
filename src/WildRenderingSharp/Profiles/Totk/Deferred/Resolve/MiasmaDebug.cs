using System.Text.RegularExpressions;
using Silk.NET.OpenGL;
using WildRenderingSharp.Logging;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>Scales each term the game's gloom lighting program (<c>field_miasma</c>) adds, to find which one lifts the gloom's black. Debugging only: delete this file, its call sites and the host's sliders.</summary>
/// <remarks>
/// The program is patched where it reads its inputs, so each scale multiplies one input: a scale of zero leaves that term out and one leaves it as the game has it.
/// </remarks>
public static class MiasmaDebug
{
    /// <summary>What each scale is for, in the order <see cref="Scales"/> holds them.</summary>
    public static readonly string[] Names =
    [
        "Ambient (material colour, times fade)",
        "Sun specular",
        "Light pre-pass layer 1 (specular)",
        "Light pre-pass layer 0 (diffuse)",
        "Pre-fog",
        "Pre-shadow",
        "Reflectance of a dielectric (0.04)",
        "Pre-misc light factor",
        "Albedo",
    ];

    /// <summary>The scales, one for each of <see cref="Names"/>; all one leaves the program as the game has it.</summary>
    public static float[] Scales { get; } = [.. Names.Select(_ => 1f)];

    internal const string Pass = "field_miasma";

    const string Uniform = "uMiasmaDebug";

    // Each patch is a pattern in the extracted program and what it becomes, with {0} the index of the scale that applies.
    static readonly (string Pattern, string Replacement, int Scale)[] Patches =
    [
        (@"fma\((temp_38[567]), 0\.2,", "fma($1, 0.2 * " + Uniform + "[{0}],", 0),
        (@"(temp_35[357] =temp_\d+ \* temp_349);", "$1 * " + Uniform + "[{0}];", 1),
        (@"(temp_19[89]|temp_200) = (temp_197\.\w);", "$1 = $2 * " + Uniform + "[{0}];", 2),
        (@"(temp_19[456]) = (temp_193\.\w);", "$1 = $2 * " + Uniform + "[{0}];", 3),
        (@"(temp_20[567]) = (temp_204\.\w);", "$1 = $2 * " + Uniform + "[{0}];", 4),
        (@"(temp_19[012]) = (temp_189\.\w);", "$1 = $2 * " + Uniform + "[{0}];", 5),
        (@"fma\((temp_306), 0\.04,", "fma($1, 0.04 * " + Uniform + "[{0}],", 6),
        (@"(temp_203) = (temp_201\.\w);", "$1 = $2 * " + Uniform + "[{0}];", 7),
        (@"(temp_16[234]) = (temp_161\.\w);", "$1 = $2 * " + Uniform + "[{0}];", 8),
    ];

    internal static string Patch(string source)
    {
        for (int i = 0; i < Patches.Length; i++)
        {
            var (pattern, replacement, scale) = Patches[i];
            var regex = new Regex(pattern);
            if (!regex.IsMatch(source))
            {
                Log.Warning($"  [miasma debug] nothing in the program matches '{Names[scale]}'");
                continue;
            }
            source = regex.Replace(source, replacement.Replace("{0}", scale.ToString()));
        }
        // The declaration goes before main.
        return source.Replace("void main()", $"uniform float {Uniform}[{Names.Length}];\nvoid main()");
    }

    internal static unsafe void Upload(GL gl, uint program)
    {
        int location = gl.GetUniformLocation(program, Uniform + "[0]");
        if (location < 0)
            return;
        fixed (float* p = Scales)
            gl.Uniform1(location, (uint)Scales.Length, p);
    }
}
