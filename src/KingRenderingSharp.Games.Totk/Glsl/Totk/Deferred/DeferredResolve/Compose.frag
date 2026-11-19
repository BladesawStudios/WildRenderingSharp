#version 450 core
uniform sampler2D t;         // one pass's fullscreen resolve
uniform sampler2D tex_id;    // the pass-ID buffer
uniform sampler2D tex_emis;  // cTex_GBuffEmission (G-buffer attachment 5)
uniform sampler2D tex_alb;   // cTex_GBuffAlbedo   (G-buffer attachment 1)
uniform sampler2D tex_gdepth; // the G-buffer's depth
uniform int uClaimEmpty;     // this pass also lights geometry no actor stamped (the terrain)
uniform float uId;
uniform float uEmission;
uniform float uEmissionExposureRcp; // see Run's remarks - keeps emission exposure-invariant
uniform float uSceneGain;
uniform int uAll;
uniform int uGFlip;
in vec2 vUV;
out vec4 fragColor;
void main() {
    float id_val = texture(tex_id, vUV).r;
    vec2 g = uGFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
    if (id_val <= 0.001) {
        if (uClaimEmpty == 0 || texture(tex_gdepth, g).r >= 1.0) discard;
    }
    else if (uAll == 0 && abs(id_val - uId) > 0.6 / 255.0) discard;

    float enable = float(int(trunc(texture(tex_alb, g).a * 255.0)) & 1);

    vec3 lit_col = texture(t, vUV).rgb;
    if (isnan(lit_col.r) || isnan(lit_col.g) || isnan(lit_col.b) || isinf(lit_col.r) || isinf(lit_col.g) || isinf(lit_col.b))
        lit_col = vec3(0.0);
    // Clamped at zero: emission is radiance added to the scene, so a correct material is never altered. This guards a decompilation artifact: negation
    // rendered as "0.0 - x" has been mis-associated into a stray "v * 0.0", leaving an emission negative for every input. Enemy_MiasmaTentacle and
    // Npc_Ganondorf_Mummy write skin emission through one (material_prog10338, temp_40 <= -0.333 whatever the uniforms) and were darkened to black.
    vec3 emission = max(texture(tex_emis, g).rgb, vec3(0.0)) * enable * uEmission * uEmissionExposureRcp;
    fragColor = vec4(lit_col * uSceneGain + emission, 1.0);
}
