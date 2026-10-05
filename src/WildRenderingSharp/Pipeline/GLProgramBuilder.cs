using System.Security.Cryptography;
using System.Text;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>Compiles and links a vertex+fragment GLSL pair into a GL program - shared by <see cref="ShaderProgramCache"/> (decompiled game shaders) and every pass class's own small fullscreen-effect shaders.</summary>
/// <remarks>
/// Linked programs are kept on disk as driver binaries (<c>glGetProgramBinary</c>) in
/// <see cref="BinaryCacheDirectory"/>, keyed by both sources and the driver's own identity, and
/// later builds of the same pair load that binary instead of compiling. Compiling is what loading
/// a model costs: a character's G-buffer, Z-only and forward programs took 1-2 seconds to link,
/// on the GL thread, every time it was loaded - the window stood still for all of it. A binary the
/// driver rejects (it updated, or the file is damaged) is simply compiled again and replaced.
/// </remarks>
public static class GLProgramBuilder
{
    /// <summary>Where linked program binaries are kept, or null to always compile. <see cref="DeferredPipeline"/> sets it to <c>_glprograms</c> under the cache root when nothing else has.</summary>
    public static string? BinaryCacheDirectory { get; set; }

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
            gl.DeleteProgram(program);
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

    static string? _driverIdentity;

    /// <summary>The file a pair's binary lives in, or null when there is no cache or the driver offers no binary formats.</summary>
    static string? BinaryCachePath(GL gl, string vertexSource, string fragmentSource)
    {
        if (BinaryCacheDirectory is not { } directory)
            return null;

        // A binary is only good for the driver that wrote it, so who that is goes into the key.
        // Empty when the driver has no binary formats at all - then there is nothing to cache.
        _driverIdentity ??= gl.GetInteger(GetPName.NumProgramBinaryFormats) > 0
            ? $"{gl.GetStringS(StringName.Vendor)}\n{gl.GetStringS(StringName.Renderer)}\n{gl.GetStringS(StringName.Version)}"
            : "";
        if (_driverIdentity.Length == 0)
            return null;

        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"{_driverIdentity}\0{vertexSource}\0{fragmentSource}"));
        return Path.Combine(directory, Convert.ToHexString(key) + ".bin");
    }

    static uint? TryLoadBinary(GL gl, string path)
    {
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        catch { return null; }
        if (file.Length <= 4)
            return null;

        var format = (GLEnum)BitConverter.ToUInt32(file, 0);
        uint program = gl.CreateProgram();
        gl.ProgramBinary(program, format, (ReadOnlySpan<byte>)file.AsSpan(4), (uint)(file.Length - 4));
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked != 0)
            return program;

        gl.DeleteProgram(program);
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

            // Written aside and moved into place, so a reader - another viewer on the same cache -
            // never sees half a file.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = $"{path}.{Environment.ProcessId}.tmp";
            File.WriteAllBytes(temp, file.AsSpan(0, (int)written + 4).ToArray());
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { /* only a cache - the program itself is fine */ }
        catch (UnauthorizedAccessException) { }
    }
}
