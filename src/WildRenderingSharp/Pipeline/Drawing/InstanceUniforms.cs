using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Drawing;

// The locations of an instanced program's instancing uniforms.
readonly record struct InstanceUniforms(int First, int Stride, int PaletteVec4s, int PaletteRepeat)
{
    public static InstanceUniforms Find(GL gl, uint program) => new(
        gl.UniformLocation(program, InstancingContract.FirstInstanceUniform),
        gl.UniformLocation(program, InstancingContract.StrideUniform),
        gl.UniformLocation(program, InstancingContract.PaletteVec4sUniform),
        gl.UniformLocation(program, InstancingContract.PaletteRepeatUniform));
}
