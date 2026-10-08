using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// <c>agl_hdr_compose</c>'s own tiny "cContext" block (decompiled as <c>fp_c3</c>), binding 4, 256 bytes - only slot 0
/// (<c>cParam</c>) is read. It shares binding 4 with <see cref="WildRenderingSharp.Profiles.Totk.Ubos.ShapeMatrixUbo"/> by design
/// (the original shaders use whatever generic UBO slot happens to be free for each pass), so the pipeline orchestrator must rebind
/// this only for the final tonemap draw, after every geometry pass that needs <c>ShpMtx</c> has already run.
/// </summary>
public sealed class HdrComposeParamsUbo : IUboBlock
{
    public const int ByteSize = 256;

    readonly Std140Block _block = new(ByteSize);

    public string Name => "cContext";
    public int BindingIndex => (int)Profiles.Totk.TotkBindings.HdrComposeParams;

    /// <summary>
    /// <c>cParam</c>: <c>out = mix(maxChannel, colour, cParam.y * (1 - s) + cParam.x)</c> where <c>s = ((r+g+b)*2/3 - 1)^2</c>.
    /// <c>(1, 0)</c> is the identity - no highlight desaturation.
    /// </summary>
    public static HdrComposeParamsUbo BuildDefault()
    {
        var ubo = new HdrComposeParamsUbo();
        ubo._block.SetSlot(0, new Vector4(1f, 0f, 0f, 0f));
        return ubo;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
