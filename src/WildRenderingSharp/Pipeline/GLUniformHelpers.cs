using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>Small uniform-setting helpers shared by every pass class's small fullscreen-effect shaders.</summary>
public static class GLUniformHelpers
{
    public static void BindTextureUniform(this GL gl, uint program, string uniform, int unit, uint textureHandle, TextureTarget target = TextureTarget.Texture2D)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(target, textureHandle);
        gl.Uniform1(gl.GetUniformLocation(program, uniform), unit);
    }

    /// <summary>Uploads a 4-row matrix (see <c>Mat4Math</c>'s remarks on the row representation) with GL's own transpose flag, so no manual transposition is needed at the call site.</summary>
    public static void SetMat4(this GL gl, uint program, string name, ReadOnlySpan<Vector4> rows)
    {
        Span<float> flat = stackalloc float[16];
        for (int i = 0; i < 4; i++)
        {
            flat[i * 4 + 0] = rows[i].X;
            flat[i * 4 + 1] = rows[i].Y;
            flat[i * 4 + 2] = rows[i].Z;
            flat[i * 4 + 3] = rows[i].W;
        }
        gl.UniformMatrix4(gl.GetUniformLocation(program, name), 1, true, flat);
    }

    public static void SetVec2(this GL gl, uint program, string name, Vector2 v) => gl.Uniform2(gl.GetUniformLocation(program, name), v.X, v.Y);
    public static void SetVec3(this GL gl, uint program, string name, Vector3 v) => gl.Uniform3(gl.GetUniformLocation(program, name), v.X, v.Y, v.Z);
    public static void SetVec4(this GL gl, uint program, string name, Vector4 v) => gl.Uniform4(gl.GetUniformLocation(program, name), v.X, v.Y, v.Z, v.W);
    public static void SetFloat(this GL gl, uint program, string name, float v) => gl.Uniform1(gl.GetUniformLocation(program, name), v);
    public static void SetInt(this GL gl, uint program, string name, int v) => gl.Uniform1(gl.GetUniformLocation(program, name), v);
}
