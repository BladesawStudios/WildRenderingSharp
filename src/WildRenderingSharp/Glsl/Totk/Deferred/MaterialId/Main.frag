#version 450 core
// Stamps the pass-ID mask where attachment 0's red byte matches a pass priority; other pixels keep their stamp.
uniform sampler2D tex_material_id;  // G-buffer attachment 0 (cTex_GBuffMaterialID)
uniform sampler2D tex_gbuf_depth;
uniform int uCount;
uniform int uPriority[16];
uniform float uPassId[16];
in vec2 vUV;
layout (location = 0) out vec4 fragColor;
void main() {
    // The G-buffer is rasterised the other way up from the mask.
    vec2 g = vec2(vUV.x, 1.0 - vUV.y);
    if (texture(tex_gbuf_depth, g).r >= 1.0)
        discard;
    int id = int(round(texture(tex_material_id, g).r * 255.0));
    for (int i = 0; i < uCount; i++) {
        if (uPriority[i] == id) {
            fragColor = vec4(uPassId[i], 0.0, 0.0, 1.0);
            return;
        }
    }
    discard;
}
