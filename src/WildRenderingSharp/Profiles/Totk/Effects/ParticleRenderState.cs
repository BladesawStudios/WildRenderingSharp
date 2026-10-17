using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>Maps an emitter's serialized blend mode and cull mode to GL state, for a pass drawn with the game's upper-left window origin.</summary>
internal static class ParticleRenderState
{
    public static void Apply(GL gl, ParticleEmitterManifest emitter)
    {
        (BlendingFactor source, BlendingFactor destination, BlendEquationModeEXT equation) = emitter.BlendMode switch
        {
            1 => (BlendingFactor.SrcAlpha, BlendingFactor.One, BlendEquationModeEXT.FuncAdd),
            2 => (BlendingFactor.SrcAlpha, BlendingFactor.One, BlendEquationModeEXT.FuncReverseSubtract),
            3 => (BlendingFactor.Zero, BlendingFactor.SrcColor, BlendEquationModeEXT.FuncAdd),
            4 => (BlendingFactor.OneMinusDstColor, BlendingFactor.One, BlendEquationModeEXT.FuncAdd),
            _ => (BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendEquationModeEXT.FuncAdd),
        };
        gl.Enable(EnableCap.Blend);
        gl.BlendFuncSeparate(source, destination, BlendingFactor.Zero, BlendingFactor.One);
        gl.BlendEquationSeparate(equation, BlendEquationModeEXT.FuncAdd);

        // The serialized modes are none, back and front. Under the game's window origin the projection is not flipped and the winding the rasterizer sees
        // is the game's own, so they are GL's faces of the same names.
        if (emitter.CullMode == 0)
        {
            gl.Disable(EnableCap.CullFace);
            return;
        }
        gl.Enable(EnableCap.CullFace);
        gl.CullFace(emitter.CullMode == 1 ? TriangleFace.Back : TriangleFace.Front);
    }

    // Puts back the state the frame's passes expect.
    public static void Restore(GL gl)
    {
        gl.Disable(EnableCap.Blend);
        gl.BlendEquationSeparate(BlendEquationModeEXT.FuncAdd, BlendEquationModeEXT.FuncAdd);
        gl.Disable(EnableCap.CullFace);
    }
}
