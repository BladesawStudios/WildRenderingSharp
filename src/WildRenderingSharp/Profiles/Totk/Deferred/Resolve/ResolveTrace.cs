using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// Records every value one pixel of a deferred resolve pass computes: the fragment program is rewritten to store its temporaries, inputs and uniform
/// slots when it shades that pixel. The decompiled programs are straight-line code, so a trace names the first operation that disagrees with its operands.
/// </summary>
internal sealed class ResolveTrace : IDisposable
{
    const uint BufferBinding = 7;

    static readonly Regex Declaration = new(
        @"^\s*(?:precise\s+)?(float|int|uint|bool|vec2|vec3|vec4|ivec2|ivec3|ivec4|uvec2|uvec3|uvec4)\s+(temp_\d+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex Input = new(@"layout\s*\(\s*location\s*=\s*\d+\s*\)\s*in\s+vec4\s+(in_attr\d+)\s*;", RegexOptions.Compiled);
    static readonly Regex UniformSlot = new(@"\b(fp_c\d+)\.data\[(\d+)\]", RegexOptions.Compiled);

    readonly GL _gl;
    readonly List<string> _names;
    readonly uint _buffer;
    readonly string _path;
    readonly string _header;
    readonly int _x, _y;

    public uint Program { get; }

    ResolveTrace(GL gl, uint program, List<string> names, string path, string header, int x, int y)
    {
        _gl = gl;
        Program = program;
        _names = names;
        _path = path;
        _header = header;
        _x = x;
        _y = y;

        _buffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _buffer);
        gl.BufferData<float>(BufferTargetARB.ShaderStorageBuffer, new float[names.Count], BufferUsageARB.DynamicRead);
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    /// <summary>Makes the traced program for <paramref name="baseName"/>, recording the pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static ResolveTrace Begin(GL gl, ShaderProgramCache programs, string baseName, string path, string header, int x, int y)
    {
        List<string> names = [];
        uint program = programs.Load(baseName, patchFragment: source => Instrument(source, names));
        return new ResolveTrace(gl, program, names, path, header, x, y);
    }

    public void Bind()
    {
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, BufferBinding, _buffer);
        _gl.Uniform2(_gl.GetUniformLocation(Program, "uWrsTraceXY"), _x, _y);
    }

    public void Finish()
    {
        _gl.MemoryBarrier(MemoryBarrierMask.ShaderStorageBarrierBit | MemoryBarrierMask.BufferUpdateBarrierBit);
        float[] values = new float[_names.Count];
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _buffer);
        _gl.GetBufferSubData<float>(BufferTargetARB.ShaderStorageBuffer, 0, values);
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        var text = new StringBuilder(_header).AppendLine();
        for (int i = 0; i < values.Length; i++)
            text.Append(_names[i]).Append(' ').AppendLine(values[i].ToString("R", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, text.ToString());
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_buffer);
        _gl.DeleteProgram(Program);
    }

    static string Instrument(string source, List<string> names)
    {
        var stores = new StringBuilder();
        void Store(string name, string expression)
        {
            stores.Append("        wrs_trace.v[").Append(names.Count).Append("] = ").Append(expression).AppendLine(";");
            names.Add(name);
        }

        Store("gl_FragCoord.x", "gl_FragCoord.x");
        Store("gl_FragCoord.y", "gl_FragCoord.y");

        foreach (Match m in Input.Matches(source))
            foreach (char c in "xyzw")
                Store($"{m.Groups[1].Value}.{c}", $"{m.Groups[1].Value}.{c}");

        foreach (var slot in UniformSlot.Matches(source).Select(m => (Block: m.Groups[1].Value, Index: m.Groups[2].Value)).Distinct())
            foreach (char c in "xyzw")
                Store($"{slot.Block}.data[{slot.Index}].{c}", $"{slot.Block}.data[{slot.Index}].{c}");

        foreach (Match m in Declaration.Matches(source))
        {
            string type = m.Groups[1].Value, name = m.Groups[2].Value;
            switch (type)
            {
                case "float":
                    Store(name, name);
                    break;
                case "int" or "uint":
                    Store(name, $"float({name})");
                    break;
                case "bool":
                    Store(name, $"{name} ? 1.0 : 0.0");
                    break;
                default:
                    int size = type[^1] - '0';
                    foreach (char c in "xyzw"[..size])
                        Store($"{name}.{c}", type[0] is 'i' or 'u' ? $"float({name}.{c})" : $"{name}.{c}");
                    break;
            }
        }

        int main = source.IndexOf("void main()", StringComparison.Ordinal);
        int ret = source.LastIndexOf("return;", StringComparison.Ordinal);
        if (main < 0 || ret < main)
            throw new InvalidOperationException("The program has no main to trace.");

        string declarations =
            $"layout (std430, binding = {BufferBinding}) buffer WrsTrace {{ float v[]; }} wrs_trace;\n" +
            "uniform ivec2 uWrsTraceXY;\n";
        string capture = "    if (ivec2(gl_FragCoord.xy) == uWrsTraceXY)\n    {\n" + stores + "    }\n";

        return source[..main] + declarations + source[main..ret] + capture + source[ret..];
    }
}
