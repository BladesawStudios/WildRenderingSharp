#version 450 core
// Stamps the pass-ID mask where attachment 0's red byte matches a pass priority; other pixels keep their stamp.
uniform sampler2D tex_material_id;  // G-buffer attachment 0 (cTex_GBuffMaterialID)
uniform sampler2D tex_gbuf_depth;
// The normalized linear depth before the host's terrain, caves and grass were drawn (1e6 where nothing was), and the planes it was made with.
uniform sampler2D tex_before_host;
uniform int uHostDrew;
uniform vec2 uNearFar;
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
    // The host's draws do not write this layer, so where they won the depth it still holds what a shape they covered wrote.
    if (uHostDrew != 0) {
        float d = texture(tex_gbuf_depth, g).r;
        float viewZ = (uNearFar.x * uNearFar.y) / (uNearFar.y * (1.0 - d) + uNearFar.x * d);
        float now = clamp((viewZ - uNearFar.x) / (uNearFar.y - uNearFar.x), 0.0, 1.0);
        float before = texture(tex_before_host, g).r;
        if (now < before - max(1e-7, before * 1e-4))
            discard;
    }
    int id = int(round(texture(tex_material_id, g).r * 255.0));
    for (int i = 0; i < uCount; i++) {
        if (uPriority[i] == id) {
            fragColor = vec4(uPassId[i], 0.0, 0.0, 1.0);
            return;
        }
    }
    discard;
}
