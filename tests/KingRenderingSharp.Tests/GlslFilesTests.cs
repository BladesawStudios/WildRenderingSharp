using System.Text.RegularExpressions;
using KingRenderingSharp.Content.Graphics;
using KingRenderingSharp.Content.Shaders;

namespace KingRenderingSharp.Tests;

public sealed class GlslFilesTests
{
    static readonly Regex DecompilerCode = new(@"\btemp_\d+\b|\bu_xlat|\bmaterial_prog\d|_extracted\b|\b_?[vf]p_c\d+\b", RegexOptions.Compiled);

    // Every layer project that ships GLSL keeps it under its own Glsl folder.
    static IEnumerable<string> LayerRoots()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KingRenderingSharp.slnx")))
            dir = dir.Parent;
        string root = dir?.FullName ?? throw new InvalidOperationException("repository root not found");
        return Directory.EnumerateDirectories(Path.Combine(root, "src"), "KingRenderingSharp*");
    }

    static IEnumerable<string> GlslRoots() => LayerRoots().Select(r => Path.Combine(r, "Glsl")).Where(Directory.Exists);

    static IEnumerable<(string Root, string File)> GlslFilesOnDisk() =>
        GlslRoots().SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => (root, f)));

    static IEnumerable<string> GlslOnDisk() => GlslFilesOnDisk().Select(g => g.File);

    [Fact]
    public void EveryGlslFileIsEmbeddedAndLoads()
    {
        foreach (var (root, file) in GlslFilesOnDisk())
        {
            string path = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            Assert.False(string.IsNullOrWhiteSpace(GlslFiles.Load(path)), path);
        }
    }

    [Fact]
    public void EveryLoadCallNamesAFileThatExists()
    {
        var call = new Regex(@"GlslFiles.Load(""([^""]+)"")");
        foreach (var cs in LayerRoots().SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories)))
            foreach (Match m in call.Matches(File.ReadAllText(cs)))
                Assert.True(GlslRoots().Any(g => File.Exists(Path.Combine(g, m.Groups[1].Value))), $"{Path.GetFileName(cs)}: {m.Groups[1].Value}");
    }

    [Fact]
    public void NoGlslIsEmbeddedInCSharp()
    {
        foreach (var cs in LayerRoots().SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories)))
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
