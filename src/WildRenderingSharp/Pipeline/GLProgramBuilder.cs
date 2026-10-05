using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>Compiles and links a vertex+fragment GLSL pair into a GL program - shared by <see cref="ShaderProgramCache"/> (decompiled game shaders) and every pass class's own small fullscreen-effect shaders.</summary>
public static class GLProgramBuilder
{
    public static uint Build(GL gl, string vertexSource, string fragmentSource, string label = "")
    {
        uint vs = CompileShader(gl, ShaderType.VertexShader, vertexSource, label + ".vert");
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSource, label + ".frag");

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
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
}
