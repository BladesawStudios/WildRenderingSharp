using System.Reflection;
using System.Runtime.Loader;

namespace WildRenderingSharp.Hosting;

/// <summary>
/// Loads <c>WildRenderingSharp.AampReader.dll</c> - a project nothing references at compile time -
/// into ONE private <see cref="AssemblyLoadContext"/>, and hands out its entry points by name.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS: the AAMP reader uses the real <c>AampLibrary</c> NuGet package, which needs
/// <c>Syroot.BinaryData</c>/<c>Syroot.Maths</c> 5.x. A process that also reads BFRES - through
/// ShaderLibrary's in-process preparation, or through the host's own BfresLibrary - needs the 2.x
/// copies of those same-named assemblies, and the two majors are binary-incompatible. They can only
/// coexist across separate load contexts; referencing the reader normally was confirmed to break
/// startup with <c>TypeLoadException: Could not load type 'Syroot.BinaryData.BinaryDataReader'</c>.
/// </para>
/// <para>
/// The renderer's sky/cloud/colour-correction postfx parsing goes through it. The public entry points are deliberately <c>byte[]</c>-in/<c>string</c>-out (JSON), so no
/// <c>Nintendo.Aamp</c>/<c>Syroot</c> type ever has to be shared between the two contexts.
/// </para>
/// <para>
/// The reader is expected in an <c>aampreader</c> folder beside the host's executable, which is
/// what <c>build/WildRenderingSharp.targets</c> produces. A host with a different layout sets
/// <see cref="Directory"/> before the first use.
/// </para>
/// </remarks>
public static class IsolatedAamp
{
    public const string AssemblyFileName = "WildRenderingSharp.AampReader.dll";

    sealed class AampLoadContext(string baseDir) : AssemblyLoadContext("WildRenderingSharp.AampReader", isCollectible: false)
    {
        /// <summary>
        /// A plain folder probe rather than <see cref="AssemblyDependencyResolver"/> - that resolved
        /// every dependency to null under <c>dotnet run</c> in practice, and the reader's project
        /// copies all of them beside it anyway (<c>CopyLocalLockFileAssemblies</c>). Framework
        /// assemblies are not there and correctly fall back to the default context.
        /// </summary>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string candidate = Path.Combine(baseDir, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }

    static readonly object Gate = new();
    static Assembly? _assembly;
    static string? _directory;

    /// <summary>
    /// The folder holding <see cref="AssemblyFileName"/> and its dependencies. Defaults to the first
    /// of <c>&lt;app&gt;/aampreader</c> and <c>&lt;app&gt;</c> that has it. Must be set before the
    /// reader is first used; later changes are ignored, because an assembly cannot be unloaded from
    /// a non-collectible context.
    /// </summary>
    public static string? Directory
    {
        get => _directory;
        set
        {
            lock (Gate)
                _directory = value;
        }
    }

    /// <summary>True once the reader is loaded, or loadable from the current <see cref="Directory"/>.</summary>
    public static bool IsAvailable => _assembly is not null || FindDirectory() is not null;

    /// <summary>A public static method of the reader, e.g. <c>("WildRenderingSharp.AampReader.SkyPostFxJson", "ParseToJson")</c>.</summary>
    /// <exception cref="FileNotFoundException">The reader is not where <see cref="Directory"/> says.</exception>
    public static MethodInfo GetEntryPoint(string typeName, string methodName)
    {
        var assembly = Load();
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"{typeName} not found in {AssemblyFileName}");
        return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{typeName}.{methodName} not found in {AssemblyFileName}");
    }

    static Assembly Load()
    {
        lock (Gate)
        {
            if (_assembly is not null)
                return _assembly;

            string dir = FindDirectory()
                ?? throw new FileNotFoundException(
                    $"{AssemblyFileName} was not found. Hosts build it into an 'aampreader' folder beside their executable " +
                    "by importing build/WildRenderingSharp.targets, or point IsolatedAamp.Directory at it.", AssemblyFileName);

            var context = new AampLoadContext(dir);
            _assembly = context.LoadFromAssemblyPath(Path.Combine(dir, AssemblyFileName));
            return _assembly;
        }
    }

    static string? FindDirectory()
    {
        string[] candidates = _directory is { Length: > 0 } explicitDir
            ? [explicitDir]
            : [Path.Combine(AppContext.BaseDirectory, "aampreader"), AppContext.BaseDirectory];
        foreach (string dir in candidates)
        {
            string full = Path.GetFullPath(dir);
            if (File.Exists(Path.Combine(full, AssemblyFileName)))
                return full;
        }
        return null;
    }
}
