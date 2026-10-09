using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Drawing;

// The locations of an instanced program's instancing uniforms.
readonly record struct InstanceUniforms(int First, int Stride, int PaletteVec4s, int PaletteRepeat)
{
    public static InstanceUniforms Find(GL gl, uint program) => new(
        gl.GetUniformLocation(program, InstancingContract.FirstInstanceUniform),
        gl.GetUniformLocation(program, InstancingContract.StrideUniform),
        gl.GetUniformLocation(program, InstancingContract.PaletteVec4sUniform),
        gl.GetUniformLocation(program, InstancingContract.PaletteRepeatUniform));
}
