using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK <c>gsys_context</c> ("Context", decompiled as <c>fp_c4</c>), binding 1, 2368 bytes.
///
/// Layout recovered statically from <c>gsys::ShaderContext::createContextUBO</c>'s 42
/// <c>declare_</c> calls (see <c>TestBench/Shaders/Decompiled/gsys_context_layout.glsl</c> and
/// <c>ENV_CONTEXT_UBO_RE_NOTES.md</c>) and verified three ways: the declarations sum to exactly
/// 2368 bytes, every Context component the deferred passes read lands on a declared field, and
/// field *meanings* for decls 0-7 and 33 are independently confirmed from
/// <c>gsys::ModelRenderContext::calcGpuViewFlush</c>.
///
/// Only the fields the render pipeline actually populates (camera + screen/quad parameters) get
/// named write methods below - the camera pose is real per-frame data, verified against the
/// fill site. Everything else in the 2368 bytes (previous-frame matrices, frustum planes, a
/// shadow-cascade-sized matrix array, a 56-vec4 parameter array, ...) is DERIVED only from array
/// shape and read pattern, not from any confirmed fill site, so it is left at its default zero
/// rather than fabricated. <see cref="Slots"/> documents where each one lives, so a future
/// live-capture cross-reference has exactly one place to fill in a real value.
/// </summary>
public sealed class ContextUbo : IUboBlock
{
    public const int ByteSize = 2368;

    /// <summary>
    /// Slot indices (16 bytes each) for every declaration in <c>gsys_context_layout.glsl</c>,
    /// named after that file's decl numbers/comments. A slot with no matching property below has
    /// no confirmed fill site and is left zeroed.
    /// </summary>
    public static class Slots
    {
        public const int View = 0;                     // decl 0, 3 rows (mat3x4)              [VERIFIED]
        public const int ViewProj = 3;                  // decl 1, 4 rows (mat4)                [VERIFIED]
        public const int Proj = 7;                      // decl 2, 4 rows (mat4)                [VERIFIED]
        public const int ViewInv = 11;                  // decl 3, 3 rows (mat3x4)              [VERIFIED]
        public const int CameraParam0 = 14;              // decl 4                                [VERIFIED]
        public const int CameraParam1 = 15;              // decl 5                                [VERIFIED]
        public const int CameraParam2 = 16;              // decl 6                                [VERIFIED]
        public const int CameraParam3 = 17;              // decl 7                                [VERIFIED]
        /// <summary>
        /// decl 8 ("unk_8" in the static layout). Deferred VERTEX shader usage pins this as
        /// (screen width, screen height, Pre*-buffer texel width, texel height) - see
        /// <see cref="BuildForCamera"/>.
        /// </summary>
        public const int ScreenResolutionAndPreTexel = 18;
        public const int Unknown9 = 19;                  // decl 9
        public const int Unknown10 = 20;                 // decl 10
        public const int PrevView = 21;                  // decl 11, 3 rows                      [DERIVED]
        public const int PrevViewProj = 24;              // decl 12, 4 rows                       [DERIVED]
        public const int PrevProj = 28;                  // decl 13, 4 rows                       [DERIVED]
        public const int PrevViewInv = 32;               // decl 14, 3 rows                       [DERIVED]
        public const int FrustumPlanes = 35;             // decl 15, 6 rows                       [DERIVED]
        public const int Unknown16 = 41;                 // decl 16
        public const int ShadowCascadeMatrices = 42;     // decl 17, 16 rows                      [DERIVED]
        public const int Unknown18 = 58;                 // decl 18
        public const int Unknown19 = 59;                 // decl 19
        public const int Unknown20 = 60;                 // decl 20, 4 rows
        public const int Unknown21 = 64;                 // decl 21
        public const int Unknown22 = 65;                 // decl 22
        public const int Unknown23 = 66;                 // decl 23 (ivec4)
        public const int Unknown24 = 67;                 // decl 24
        public const int LargeParamArray = 68;           // decl 25, 56 rows                      [DERIVED]
        public const int PackedFlags = 124;               // decl 26-29 packed into one slot (uint,uint,bool,bool)
        public const int Unknown30 = 125;                 // decl 30, 3 rows
        public const int AuxArray = 128;                  // decl 31, 16 rows                      [DERIVED]
        public const int Unknown32 = 144;                 // decl 32
        /// <summary>decl 33.x copied from ModelRenderContext+0x5d8 [VERIFIED]; .y/.z/.w = decls 34-36.</summary>
        public const int PackedIds = 145;
        /// <summary>decl 37 (.xy, screen size in pixels) / decl 38 (.zw, screen size as ints).</summary>
        public const int ScreenSize = 146;
        /// <summary>decl 39/40/41 - in practice always the fullscreen-quad generator constants; see <see cref="BuildForCamera"/>.</summary>
        public const int FullscreenQuadParams = 147;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "Context";
    public int BindingIndex => (int)TotkBindings.Camera;
    public int SizeBytes => ByteSize;

    /// <summary>
    /// Populates the camera-derived fields of a Context block. <paramref name="view"/> is 3 rows
    /// (mat3x4), everything else is already-computed 4x4 data (<paramref name="viewProj"/> =
    /// proj * [view;0,0,0,1], <paramref name="viewInv"/> = the inverse of that same 4x4, taking
    /// only its first 3 rows) - the actual matrix math lives in
    /// <c>WildRenderingSharp.Rendering.Camera</c>/<c>Mat4Math</c>, not here; this method only packs
    /// already-computed values into the std140 layout, the same job <c>build_context</c> does.
    /// </summary>
    public static ContextUbo BuildForCamera(
        ReadOnlySpan<Vector4> view, ReadOnlySpan<Vector4> viewProj, ReadOnlySpan<Vector4> proj,
        ReadOnlySpan<Vector4> viewInv, float aspect, float tanHalfFovY, float near, float far,
        Vector2 preTexel)
    {
        if (view.Length != 3 || viewInv.Length != 3)
            throw new ArgumentException("view/viewInv must have exactly 3 rows (mat3x4)");
        if (viewProj.Length != 4 || proj.Length != 4)
            throw new ArgumentException("viewProj/proj must have exactly 4 rows (mat4)");

        var ctx = new ContextUbo();
        ctx._block.WriteRows(Slots.View, view);
        ctx._block.WriteRows(Slots.ViewProj, viewProj);
        ctx._block.WriteRows(Slots.Proj, proj);
        ctx._block.WriteRows(Slots.ViewInv, viewInv);

        // decl 4: (near, far, near/far, 1 - near/far)                                [VERIFIED]
        ctx._block.SetSlot(Slots.CameraParam0, near, far, near / far, 1f - near / far);
        // decl 5: (1/(far-near), near/(far-near), aspect, 1/aspect)                  [VERIFIED]
        ctx._block.SetSlot(Slots.CameraParam1, 1f / (far - near), near / (far - near), aspect, 1f / aspect);
        // decl 6: (far-near, 1/(1-near/far), 0, 0)                                   [VERIFIED]
        ctx._block.SetSlot(Slots.CameraParam2, far - near, 1f / (1f - near / far), 0f, 0f);
        // decl 7: (aspect*tanHalfFovY, tanHalfFovY, fovY, 0) - .xy are view-ray scales
        // (out_attr1 = -2*x, 2*y); .z is vertical FOV in radians (sead::PerspectiveProjection::getFovy
        // written by calcGpuViewFlush, read by chara_skin/chara_hair/chara_eye/etc. for cel-shading
        // and screen-space normal reconstruction); .w is 0.                          [VERIFIED]
        float fovY = 2f * MathF.Atan(tanHalfFovY);
        ctx._block.SetSlot(Slots.CameraParam3, aspect * tanHalfFovY, tanHalfFovY, fovY, 0f);

        // decl 8: (screen width, screen height, Pre*-buffer texel width, texel height). The
        // deferred vertex shader emits out_attr2 = uv * this.xy * 0.5 + 0.5 and the fragment
        // shader recovers the sub-texel fraction from it - that only holds together if .xy is
        // the screen resolution in pixels.
        ctx._block.SetSlot(Slots.ScreenResolutionAndPreTexel,
            1f / preTexel.X, 1f / preTexel.Y, preTexel.X, preTexel.Y);

        // decl 37 (.xy, screen size in pixels as floats) / decl 38 (.zw, screen size as 32-bit ints).
        // Read by material_prog15554 (Zelda face), eye shaders, and 40+ other G-buffer programs
        // to compute 1D pixel coordinates (y * width + x).
        ctx._block.SetSlot(Slots.ScreenSize,
            1f / preTexel.X, 1f / preTexel.Y,
            BitConverter.Int32BitsToSingle((int)(1f / preTexel.X)),
            BitConverter.Int32BitsToSingle((int)(1f / preTexel.Y)));

        // decl 39/40 (slot 147): the fullscreen-quad generator in the deferred vertex shader reads
        // gl_Position.x = (vid&1) * this.x * 2 - 1, gl_Position.y = 1 - (vid>>1) * this.y * 2, and
        // gl_Position.z = this.z when the material-id term is zero - so (1,1,0.5,0) emits a
        // correct full-screen quad from a 4-vertex triangle strip.
        ctx._block.SetSlot(Slots.FullscreenQuadParams, 1f, 1f, 0.5f, 0f);

        return ctx;
    }

    /// <summary>
    /// The G-buffer pass renders through a Y-flipped projection (NVN's upper-left-origin window
    /// convention); every later pass (resolve, forward) undoes that and works in the true GL
    /// orientation. Returns a new block identical to this one except <see cref="Slots.Proj"/> and
    /// <see cref="Slots.ViewProj"/> have their second row negated.
    /// </summary>
    public ContextUbo WithFlippedProjectionY()
    {
        var flipped = new ContextUbo();
        for (int slot = 0; slot < _block.SizeBytes / 16; slot++)
            flipped._block.SetSlot(slot, _block.GetSlot(slot));

        void FlipRow1(int firstSlot) => flipped._block.SetSlot(firstSlot + 1, -flipped._block.GetSlot(firstSlot + 1));
        FlipRow1(Slots.Proj);
        FlipRow1(Slots.ViewProj);
        return flipped;
    }

    public void WriteTo(Span<byte> destination) => _block.WriteTo(destination);
    public byte[] ToByteArray() => _block.ToByteArray();
}
