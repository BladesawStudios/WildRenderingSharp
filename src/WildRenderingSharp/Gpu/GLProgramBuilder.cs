using System.Security.Cryptography;
using System.Text;
using Silk.NET.OpenGL;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Gpu;

/// <summary>
/// Compiles and links a vertex+fragment GLSL pair into a GL program - shared by <see cref="ShaderProgramCache"/> (decompiled game
/// shaders) and every pass class's own small fullscreen-effect shaders.
/// </summary>
internal static class GLProgramBuilder
{
    sealed class BinaryCache
    {
        public string? Directory;
        public string? DriverIdentity;
    }

    // Keeps compiled programs in the directory for this context, unless it already has one.
    public static void UseBinaryCache(GL gl, string directory) => ContextState<BinaryCache>.For(gl).Directory ??= directory;

    /// <summary>
    /// A program kept under a name, so that a program already built needs none of the work of making its source: no reading it, no cleaning it, no patching it and no hashing it.
    /// Returns 0 when <paramref name="make"/> says there is no such program (a pair that cannot be made merged, say), which is remembered too.
    /// </summary>
    /// <remarks>
    /// The identity has to say everything the source depends on: the program's name, which form of it this is, the files it came from as they stood, and the version of the code
    /// that patches them. A binary already kept under the hash of the source it came to is taken over, so a cache from before this one is not built again.
    /// </remarks>
    public static uint BuildNamed(GL gl, string identity, Func<(string Vertex, string Fragment)?> make, string label)
    {
        string? path = NamedPath(gl, identity);
        if (path is not null)
        {
            if (File.Exists(path + ".none"))
                return 0;
            if (TryLoadBinary(gl, path) is uint cached)
                return cached;
        }

        if (make() is not var (vertex, fragment))
        {
            if (path is not null)
                try { File.WriteAllBytes(path + ".none", []); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return 0;
        }

        // A binary from the days of the cache that was keyed by source, taken over by moving it.
        string? bySource = BinaryCachePath(gl, vertex, fragment);
        if (path is not null && bySource is not null && File.Exists(bySource))
        {
            try
            {
                File.Move(bySource, path, overwrite: true);
                if (TryLoadBinary(gl, path) is uint moved)
                    return moved;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        uint program = BuildUnkept(gl, vertex, fragment, label, retrievable: path is not null);
        if (path is not null)
            SaveBinary(gl, program, path);
        return program;
    }

    static string? NamedPath(GL gl, string identity)
    {
        var cache = ContextState<BinaryCache>.For(gl);
        if (cache.Directory is not { } directory)
            return null;
        cache.DriverIdentity ??= gl.GetInteger(GetPName.NumProgramBinaryFormats) > 0
            ? $"{gl.GetStringS(StringName.Vendor)}\n{gl.GetStringS(StringName.Renderer)}\n{gl.GetStringS(StringName.Version)}"
            : "";
        if (cache.DriverIdentity.Length == 0)
            return null;

        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"{cache.DriverIdentity}\0named\0{identity}"));
        return Path.Combine(directory, "n" + Convert.ToHexString(key) + ".bin");
    }

    static uint BuildUnkept(GL gl, string vertexSource, string fragmentSource, string label, bool retrievable)
    {
        uint vs = CompileShader(gl, ShaderType.VertexShader, vertexSource, label + ".vert");
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSource, label + ".frag");

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        if (retrievable)
            gl.ProgramParameter(program, ProgramParameterPName.BinaryRetrievableHint, 1);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        gl.DetachShader(program, vs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(program);
            gl.ReleaseProgram(program);
            throw new InvalidOperationException($"Failed to link shader program '{label}':\n{log}");
        }
        return program;
    }

    public static uint Build(GL gl, string vertexSource, string fragmentSource, string label = "")
    {
        string? cachePath = BinaryCachePath(gl, vertexSource, fragmentSource);
        if (cachePath is not null && TryLoadBinary(gl, cachePath) is uint cached)
            return cached;

        uint vs = CompileShader(gl, ShaderType.VertexShader, vertexSource, label + ".vert");
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSource, label + ".frag");

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        if (cachePath is not null)
            gl.ProgramParameter(program, ProgramParameterPName.BinaryRetrievableHint, 1);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        gl.DetachShader(program, vs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(program);
            gl.ReleaseProgram(program);
            throw new InvalidOperationException($"Failed to link shader program '{label}':\n{log}");
        }

        if (cachePath is not null)
            SaveBinary(gl, program, cachePath);
        return program;
    }

    static uint CompileShader(GL gl, ShaderType type, string source, string label)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"Failed to compile '{label}':\n{log}");
        }
        return shader;
    }

    static string? BinaryCachePath(GL gl, string vertexSource, string fragmentSource)
    {
        var cache = ContextState<BinaryCache>.For(gl);
        if (cache.Directory is not { } directory)
            return null;

        // A binary is only good for the driver that wrote it, so who that is goes into the key.
        // Empty when the driver has no binary formats at all - then there is nothing to cache.
        cache.DriverIdentity ??= gl.GetInteger(GetPName.NumProgramBinaryFormats) > 0
            ? $"{gl.GetStringS(StringName.Vendor)}\n{gl.GetStringS(StringName.Renderer)}\n{gl.GetStringS(StringName.Version)}"
            : "";
        if (cache.DriverIdentity.Length == 0)
            return null;

        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"{cache.DriverIdentity}\0{vertexSource}\0{fragmentSource}"));
        return Path.Combine(directory, Convert.ToHexString(key) + ".bin");
    }

    static uint? TryLoadBinary(GL gl, string path)
    {
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        // No cached binary, or one that cannot be read, is compiled from source instead.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (file.Length <= 4)
            return null;

        var format = (GLEnum)BitConverter.ToUInt32(file, 0);
        uint program = gl.CreateProgram();
        gl.ProgramBinary(program, format, (ReadOnlySpan<byte>)file.AsSpan(4), (uint)(file.Length - 4));
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked != 0)
            return program;

        gl.ReleaseProgram(program);
        try { File.Delete(path); } catch { /* compiled again and rewritten by the caller */ }
        return null;
    }

    static void SaveBinary(GL gl, uint program, string path)
    {
        try
        {
            gl.GetProgram(program, ProgramPropertyARB.ProgramBinaryLength, out int length);
            if (length <= 0)
                return;

            byte[] file = new byte[length + 4];
            uint written;
            GLEnum format;
            unsafe
            {
                fixed (byte* binary = &file[4])
                    gl.GetProgramBinary(program, (uint)length, &written, &format, binary);
            }
            if (written == 0)
                return;
            BitConverter.TryWriteBytes(file.AsSpan(0, 4), (uint)format);

            AtomicFile.WriteAllBytes(path, file.AsSpan(0, (int)written + 4));
        }
        // Only a cache - the program itself is fine.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
