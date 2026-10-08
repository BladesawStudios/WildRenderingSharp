using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// Manual, individually verified corrections for game rendering behaviour the shader-driven pipeline cannot derive; see <see
/// cref="Rendering.LightingContext.EnableKnownMaterialFixes"/>.
/// </summary>
public sealed class KnownMaterialFixes : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    public const string EyeVisibilityMaskTextureName = "Cmn_Enemy_DungeonBoss_Eye_Alb";

    public static bool NeedsEyeVisibilityMaskFix(LoadedShape shape)
    {
        foreach (var sampler in shape.GBufferSamplers)
            if (sampler.Key == "_a0" && string.Equals(sampler.Texture.Name, EyeVisibilityMaskTextureName, StringComparison.Ordinal))
                return true;
        return false;
    }

    const string VertexSource = """
        #version 450 core
        layout (location = 0) in vec4 aPosition;
        layout (location = 4) in vec4 aBlendWeight0;
        layout (location = 5) in vec4 aBlendWeight1;
        layout (location = 6) in vec4 aBlendIndex0;
        layout (location = 7) in vec4 aBlendIndex1;

        layout (binding = 2, std140) uniform _Mtx { vec4 data[4096]; } bones;

        uniform mat4 uMVP;      // proj_flipped * view * model - SKIN_COUNT 0 only (positions are pre-posed)
        uniform mat4 uViewProj; // proj_flipped * view - the palette already carries the model transform
        uniform int uSkinCount;

        const int kBoneSlots = 4096 / 3;

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
        uniform sampler2D tex_alb;   // cTex_GBuffAlbedo  (G-buffer attachment 1) - Scene is already in this texture's own flipped space, no extra flip needed
        uniform sampler2D tex_emis;  // cTex_GBuffEmission (G-buffer attachment 5)
        uniform vec2 uViewportSize;
        uniform float uEmission;
        uniform float uEmissionExposureRcp;
        layout (location = 0) out vec4 fragColor;

        void main()
        {
            vec2 uv = gl_FragCoord.xy / uViewportSize;
            vec3 albedo = texture(tex_alb, uv).rgb;

            // Green is the visibility mask: a continuous multiplier (0 = eye not visible, 1 = fully visible), so a partially green pixel gets proportionally dimmed emission.
            // Red plays a separate role (it suppresses a distinct over-brightness artifact) and must not be folded in.
            float mask = albedo.g;
            vec3 emission = max(texture(tex_emis, uv).rgb, vec3(0.0)) * uEmission * uEmissionExposureRcp;
            fragColor = vec4(emission * mask, 1.0);
        }
        """;

    public KnownMaterialFixes(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "known_material_fix_eye_visibility_mask");
    }

    public unsafe void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups,
        ReadOnlySpan<Vector4> viewProjFlippedRows, float emissionScale, float exposure)
    {
        var flaggedGroups = groups
            .Select(g => (Group: g, Flagged: g.Shapes.Where(NeedsEyeVisibilityMaskFix).ToList()))
            .Where(x => x.Flagged.Count > 0)
            .ToList();
        if (flaggedGroups.Count == 0)
            return;

        targets.BindColorAndDepthTarget(targets.Scene, targets.GBufferDepth);
        // Depth-tested with a small negative polygon offset, the standard decal technique. This redraws geometry the G-buffer already rasterised through a hand-written vertex shader
        // whose clip position can land a few ULPs off the stored depth (see ForwardPass). A bare LEQUAL test flickered (z-fighting); no test at all drew over genuine occluders
        // (eyelid and head geometry). The offset nudges depth toward the camera by a couple of the buffer's smallest steps: enough to beat the self-mismatch, far short of a real occluder.
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-1f, -4f);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);

        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tex_alb", 0, targets.GBuffer[1].Handle);
        _gl.BindTextureUniform(_program, "tex_emis", 1, targets.GBuffer[5].Handle);
        _gl.SetVec2(_program, "uViewportSize", new Vector2(targets.Width, targets.Height));
        _gl.SetFloat(_program, "uEmission", emissionScale);
        _gl.SetFloat(_program, "uEmissionExposureRcp", exposure > 1e-4f ? 1f / exposure : 1f);
        _gl.SetMat4(_program, "uViewProj", viewProjFlippedRows);

        foreach (var (group, flagged) in flaggedGroups)
        {
            // A character's eye fix: batches of placed world objects have none to fix and no single model matrix to draw it with.
            if (group.Batch is not null)
                continue;
            group.BindUbos(resources);
            var model = Rendering.Mat4Math.ToMat4(group.ModelMatrixRows);
            var mvp = Rendering.Mat4Math.Multiply(viewProjFlippedRows, model);

            foreach (var shape in flagged)
            {
                _gl.SetMat4(_program, "uMVP", mvp);
                _gl.Uniform1(_gl.GetUniformLocation(_program, "uSkinCount"), shape.VertexSkinCount);
                _gl.BindVertexArray(shape.PassIdVao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)shape.IndexCount, DrawElementsType.UnsignedInt, null);
            }
        }

        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
