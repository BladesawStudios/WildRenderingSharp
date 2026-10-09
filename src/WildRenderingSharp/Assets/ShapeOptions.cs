using System.Text.Json;

namespace WildRenderingSharp.Assets;

/// <summary>A shape's manifest extensions and its static options, read from the options file exported beside its geometry.</summary>
sealed record ShapeOptions(Dictionary<string, string> Tags, bool HidesNormalPass)
{
    const string HideNormalPass = "o_enable_hide_normal_pass=True";

    // The manifest's extra string and number fields, plus each key=value line of the options file as option.key.
    public static ShapeOptions Read(ShapeManifestEntry shape, string dataDirectory)
    {
        var tags = shape.Extensions.Where(e => e.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .ToDictionary(e => e.Key, e => e.Value.ToString());

        string path = Path.Combine(dataDirectory, $"{shape.Name}_options.txt");
        if (!File.Exists(path))
            return new ShapeOptions(tags, false);

        bool hidesNormalPass = false;
        foreach (string line in File.ReadLines(path))
        {
            hidesNormalPass |= line.Trim().Equals(HideNormalPass, StringComparison.OrdinalIgnoreCase);
            int eq = line.IndexOf('=');
            if (eq > 0)
                tags["option." + line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return new ShapeOptions(tags, hidesNormalPass);
    }
}
