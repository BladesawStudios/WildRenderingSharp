using System.Text.RegularExpressions;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Tests;

public sealed class GlslFilesTests
{
    static readonly Regex DecompilerCode = new(@"\btemp_\d+\b|\bu_xlat|\bmaterial_prog\d|_extracted\b|\b_?[vf]p_c\d+\b", RegexOptions.Compiled);

    static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WildRenderingSharp.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repository root not found"), "src", "WildRenderingSharp");
    }

    static IEnumerable<string> GlslOnDisk() =>
        Directory.EnumerateFiles(Path.Combine(SourceRoot(), "Glsl"), "*", SearchOption.AllDirectories);

    [Fact]
    public void EveryGlslFileIsEmbeddedAndLoads()
    {
        string glslRoot = Path.Combine(SourceRoot(), "Glsl");
        foreach (var file in GlslOnDisk())
        {
            string path = Path.GetRelativePath(glslRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            Assert.False(string.IsNullOrWhiteSpace(GlslFiles.Load(path)), path);
        }
    }

    [Fact]
    public void EveryLoadCallNamesAFileThatExists()
    {
        string glslRoot = Path.Combine(SourceRoot(), "Glsl");
        var call = new Regex(@"GlslFiles\.Load\(""([^""]+)""\)");
        foreach (var cs in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
            foreach (Match m in call.Matches(File.ReadAllText(cs)))
                Assert.True(File.Exists(Path.Combine(glslRoot, m.Groups[1].Value)), $"{Path.GetFileName(cs)}: {m.Groups[1].Value}");
    }

    [Fact]
    public void NoGlslIsEmbeddedInCSharp()
    {
        foreach (var cs in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(cs);
            Assert.DoesNotMatch(@"(?<!StartsWith\()""#version|""""""\s*\n\s*#version", text);
        }
    }

    [Fact]
    public void NoDecompiledShaderCodeIsCommitted()
    {
        foreach (var file in GlslOnDisk())
        {
            var code = string.Join('\n', File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//")));
            Assert.False(DecompilerCode.IsMatch(code), $"{file} contains decompiler output; game shaders must come from the prepared cache.");
        }
    }
}
