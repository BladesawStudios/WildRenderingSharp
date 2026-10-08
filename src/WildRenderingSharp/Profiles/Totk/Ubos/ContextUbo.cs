using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>TotK <c>gsys_context</c> ("Context", decompiled as <c>fp_c4</c>), binding 1, 2368 bytes.</summary>
public sealed class ContextUbo : IUboBlock
{
    public const int ByteSize = 2368;

    /// <summary>
    /// Slot indices (16 bytes each) for every declaration in <c>gsys_context_layout.glsl</c>, named after its decl numbers and
    /// comments.
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
        public const int PackedIds = 145;
        public const int ScreenSize = 146;
        public const int FullscreenQuadParams = 147;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "Context";
    public int BindingIndex => (int)TotkBindings.Camera;

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
        // decl 7: (aspect*tanHalfFovY, tanHalfFovY, fovY, 0). .xy are view-ray scales (out_attr1 = -2*x, 2*y); .z is the vertical FOV in radians
        // (sead::PerspectiveProjection::getFovy, written by calcGpuViewFlush, read by chara_skin, chara_hair, chara_eye and others for cel-shading
        // and normal reconstruction).                                                [VERIFIED]
        float fovY = 2f * MathF.Atan(tanHalfFovY);
        ctx._block.SetSlot(Slots.CameraParam3, aspect * tanHalfFovY, tanHalfFovY, fovY, 0f);

        // decl 8: (screen width, screen height, Pre*-buffer texel width, texel height). The deferred vertex shader emits out_attr2 = uv * this.xy * 0.5 + 0.5
        // and the fragment shader recovers the sub-texel fraction from it, which only works if .xy is the resolution in pixels.
        ctx._block.SetSlot(Slots.ScreenResolutionAndPreTexel,
            1f / preTexel.X, 1f / preTexel.Y, preTexel.X, preTexel.Y);

        // decl 37 (.xy, screen size in pixels as floats) and decl 38 (.zw, as 32-bit ints), read by material_prog15554 (Zelda face), the eye shaders and 40+
        // other G-buffer programs for 1D pixel coordinates (y * width + x).
        ctx._block.SetSlot(Slots.ScreenSize,
            1f / preTexel.X, 1f / preTexel.Y,
            BitConverter.Int32BitsToSingle((int)(1f / preTexel.X)),
            BitConverter.Int32BitsToSingle((int)(1f / preTexel.Y)));

        // decl 39/40 (slot 147): the deferred vertex shader's fullscreen-quad generator reads gl_Position.x = (vid&1) * this.x * 2 - 1, y = 1 - (vid>>1) * this.y * 2,
        // and z = this.z when the material-id term is zero, so (1,1,0.5,0) emits a full-screen quad from a 4-vertex strip.
        ctx._block.SetSlot(Slots.FullscreenQuadParams, 1f, 1f, 0.5f, 0f);

        return ctx;
    }

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

    public byte[] ToByteArray() => _block.ToByteArray();
}
