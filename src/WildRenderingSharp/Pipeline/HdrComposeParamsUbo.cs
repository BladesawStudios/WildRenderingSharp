using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// <c>agl_hdr_compose</c>'s own tiny "cContext" block (decompiled as <c>fp_c3</c>), binding 4,
/// 256 bytes - only slot 0 (<c>cParam</c>) is read. It shares binding 4 with
/// <see cref="WildRenderingSharp.Profiles.Totk.Ubos.ShapeMatrixUbo"/> by design (the original shaders
/// use whatever generic UBO slot happens to be free for each pass), so the pipeline orchestrator
/// must rebind this only for the final tonemap draw, after every geometry pass that needs
/// <c>ShpMtx</c> has already run.
/// </summary>
public sealed class HdrComposeParamsUbo : IUboBlock
{
    public const int ByteSize = 256;

    readonly Std140Block _block = new(ByteSize);

    public string Name => "cContext";
    public int BindingIndex => (int)Profiles.Totk.TotkBindings.HdrComposeParams;
    public int SizeBytes => ByteSize;

    /// <summary>
    /// <c>cParam</c>: <c>out = mix(maxChannel, colour, cParam.y * (1 - s) + cParam.x)</c> where
    /// <c>s = ((r+g+b)*2/3 - 1)^2</c>. <c>(1, 0)</c> is the identity - no highlight desaturation.
    /// The real per-scene value lives in an <c>agl::pfx</c> post config WildRenderingSharp doesn't read yet.
    /// </summary>
    public static HdrComposeParamsUbo BuildDefault()
    {
        var ubo = new HdrComposeParamsUbo();
        ubo._block.SetSlot(0, new Vector4(1f, 0f, 0f, 0f));
        return ubo;
    }

    public void WriteTo(Span<byte> destination) => _block.WriteTo(destination);
    public byte[] ToByteArray() => _block.ToByteArray();
}
