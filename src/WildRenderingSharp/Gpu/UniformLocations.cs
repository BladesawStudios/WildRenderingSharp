using Silk.NET.OpenGL;

namespace WildRenderingSharp.Gpu;

/// <summary>
/// Uniform locations by program and name, asked of the driver once, since a lookup costs about a microsecond and a frame sets a few
/// hundred. Programs must be deleted through ReleaseProgram, because the driver reuses a deleted program's name.
/// </summary>
internal static class UniformLocations
{
    sealed class Cache
    {
        public readonly Dictionary<uint, Dictionary<string, int>> Programs = [];
    }

    // The location of the uniform, or -1 when the program has none by that name (which is cached too).
    public static int UniformLocation(this GL gl, uint program, string name)
    {
        var programs = ContextState<Cache>.For(gl).Programs;
        lock (programs)
        {
            if (!programs.TryGetValue(program, out var names))
                programs[program] = names = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!names.TryGetValue(name, out int location))
                names[name] = location = gl.GetUniformLocation(program, name);
            return location;
        }
    }

    public static void ReleaseProgram(this GL gl, uint program)
    {
        var programs = ContextState<Cache>.For(gl).Programs;
        lock (programs)
            programs.Remove(program);
        gl.DeleteProgram(program);
    }
}
