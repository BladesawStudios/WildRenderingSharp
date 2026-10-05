using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Draws one shape's silhouette as a flat, alpha-blended overlay on the finished frame - the
/// Material Inspector's "which object is this row" hover highlight. Draws directly into the
/// FINAL tonemapped <see cref="RenderTargets.Ldr"/> buffer, with NO depth test at all, so it
/// reads as sitting above literally everything - even something occluding the hovered object
/// from the current view - rather than going through the whole deferred pipeline again (a hover
/// highlight has to track the mouse instantly and cheaply, not wait on a full re-render) or being
/// just another translucent layer that bloom/tonemapping could wash out or that nearer geometry
/// could hide.
///
/// Reuses <see cref="PassIdMaskPass"/>'s own GPU-skinning vertex logic verbatim (see its remarks
/// for why this must be skinned, not a bind-pose silhouette) so an animated shape's highlight
/// tracks its actual posed silhouette.
/// </summary>
public sealed class HighlightOverlayPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    const string VertexSource = """
        #version 450 core
        layout (location = 0) in vec4 aPosition;
        layout (location = 4) in vec4 aBlendWeight0;
        layout (location = 5) in vec4 aBlendWeight1;
        layout (location = 6) in vec4 aBlendIndex0;
        layout (location = 7) in vec4 aBlendIndex1;

        layout (binding = 2, std140) uniform _Mtx { vec4 data[4096]; } bones;

        uniform mat4 uMVP;      // proj * view * model - SKIN_COUNT 0 only (positions are pre-posed)
        uniform mat4 uViewProj; // proj * view - the palette already carries the model transform
        uniform int uSkinCount;

        const int kBoneSlots = 4096 / 3; // the _Mtx array holds 3 vec4 rows per bone

        vec3 skinOne(vec3 p, float packedIndex)
        {
            int slot = clamp(floatBitsToInt(packedIndex) & 0xFFFF, 0, kBoneSlots - 1);
            vec4 v = vec4(p, 1.0);
            return vec3(dot(v, bones.data[slot * 3 + 0]),
                        dot(v, bones.data[slot * 3 + 1]),
                        dot(v, bones.data[slot * 3 + 2]));
        }

        void main()
        {
            vec3 p = aPosition.xyz;
            if (uSkinCount == 0)
            {
                gl_Position = uMVP * vec4(p, 1.0);
                return;
            }
            if (uSkinCount == 1)
            {
                gl_Position = uViewProj * vec4(skinOne(p, aBlendIndex0.x), 1.0);
                return;
            }
            vec3 skinned = skinOne(p, aBlendIndex0.x) * aBlendWeight0.x;
            skinned += skinOne(p, aBlendIndex0.y) * aBlendWeight0.y;
            if (uSkinCount >= 3) skinned += skinOne(p, aBlendIndex0.z) * aBlendWeight0.z;
            if (uSkinCount >= 4) skinned += skinOne(p, aBlendIndex0.w) * aBlendWeight0.w;
            if (uSkinCount >= 5) skinned += skinOne(p, aBlendIndex1.x) * aBlendWeight1.x;
            if (uSkinCount >= 6) skinned += skinOne(p, aBlendIndex1.y) * aBlendWeight1.y;
            if (uSkinCount >= 7) skinned += skinOne(p, aBlendIndex1.z) * aBlendWeight1.z;
            if (uSkinCount >= 8) skinned += skinOne(p, aBlendIndex1.w) * aBlendWeight1.w;
            gl_Position = uViewProj * vec4(skinned, 1.0);
        }
        """;

    const string FragmentSource = """
        #version 450 core
        uniform vec4 uColor;
        layout (location = 0) out vec4 fragColor;
        void main() { fragColor = uColor; }
        """;

    public HighlightOverlayPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "highlight_overlay");
    }

    /// <param name="mvpRows"><c>proj @ [view;0,0,0,1] @ model</c> - skin count 0 shapes.</param>
    /// <param name="viewProjRows"><c>proj @ [view;0,0,0,1]</c> - skinned shapes (palette already includes the model transform). Same TRUE (unflipped) matrices <see cref="PassIdMaskPass"/> uses - <see cref="RenderTargets.Ldr"/> (where this draws) is in that same true orientation.</param>
    /// <remarks>
    /// No depth test at all, and drawn into the FINAL tonemapped <see cref="RenderTargets.Ldr"/>
    /// buffer rather than anywhere earlier in the chain - "highlight this object" should mean
    /// visible above literally everything (even something occluding it from this angle) and
    /// untouched by exposure/bloom/tonemapping, not just another translucent layer competing with
    /// the rest of the scene for the same treatment.
    /// </remarks>
    public unsafe void Draw(GLResourceCache resources, RenderTargets targets, ActorDrawGroup owningActor, LoadedShape shape, ReadOnlySpan<Vector4> mvpRows, ReadOnlySpan<Vector4> viewProjRows, Vector4 color)
    {
        // A skinned highlighted shape reads its bone pose from _Mtx (binding 2) exactly like every
        // other pass - has to be THIS shape's own actor's buffer, not whichever one happened to be
        // bound last (that's why this takes the owning ActorDrawGroup, not just the shape).
        owningActor.BindUbos(resources);
        targets.BindColorTarget(targets.Ldr);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.Zero);
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);

        _gl.UseProgram(_program);
        _gl.SetMat4(_program, "uMVP", mvpRows);
        _gl.SetMat4(_program, "uViewProj", viewProjRows);
        _gl.SetVec4(_program, "uColor", color);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uSkinCount"), shape.VertexSkinCount);

        _gl.BindVertexArray(shape.PassIdVao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)shape.IndexCount, DrawElementsType.UnsignedInt, null);

        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
