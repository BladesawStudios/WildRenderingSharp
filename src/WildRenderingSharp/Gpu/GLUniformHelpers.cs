using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Gpu;

/// <summary>Small GL helpers shared by the passes: uniform setting, interface-block and sampler assignment, and texture sampling state.</summary>
internal static class GLUniformHelpers
{
    public static bool BindUniformBlock(this GL gl, uint program, string name, uint binding)
    {
        uint index = gl.GetUniformBlockIndex(program, name);
        if (index == 0xFFFFFFFFu)
            return false;
        gl.UniformBlockBinding(program, index, binding);
        return true;
    }

    public static void SetSamplerUnit(this GL gl, uint program, string name, int unit)
    {
        int location = gl.UniformLocation(program, name);
        if (location >= 0)
            gl.Uniform1(location, unit);
    }

    public static void SetSampling(this GL gl, TextureTarget target, GLEnum filter, GLEnum wrap)
    {
        gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)filter);
        gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)filter);
        gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)wrap);
        gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)wrap);
    }

    public static void BindTextureAt(this GL gl, int unit, uint textureHandle, TextureTarget target = TextureTarget.Texture2D)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(target, textureHandle);
    }

    public static void BindTextureUniform(this GL gl, uint program, string uniform, int unit, uint textureHandle, TextureTarget target = TextureTarget.Texture2D)
    {
        gl.BindTextureAt(unit, textureHandle, target);
        gl.Uniform1(gl.UniformLocation(program, uniform), unit);
    }

    // System.Numerics stores the transpose of a GL matrix, which is exactly GL's column-major layout, so it goes up untransposed.
    public static void SetMat4(this GL gl, uint program, string name, Matrix4x4 m) =>
        gl.UniformMatrix4(gl.UniformLocation(program, name), 1, false, MemoryMarshal.CreateReadOnlySpan(ref m.M11, 16));

    public static void SetVec2(this GL gl, uint program, string name, Vector2 v) => gl.Uniform2(gl.UniformLocation(program, name), v.X, v.Y);
    public static void SetVec3(this GL gl, uint program, string name, Vector3 v) => gl.Uniform3(gl.UniformLocation(program, name), v.X, v.Y, v.Z);
    public static void SetVec4(this GL gl, uint program, string name, Vector4 v) => gl.Uniform4(gl.UniformLocation(program, name), v.X, v.Y, v.Z, v.W);
    public static void SetFloat(this GL gl, uint program, string name, float v) => gl.Uniform1(gl.UniformLocation(program, name), v);
    public static void SetInt(this GL gl, uint program, string name, int v) => gl.Uniform1(gl.UniformLocation(program, name), v);
}
