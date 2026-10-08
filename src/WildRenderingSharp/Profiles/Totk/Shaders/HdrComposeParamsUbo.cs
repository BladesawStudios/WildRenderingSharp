using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// <c>agl_hdr_compose</c>'s own tiny "cContext" block (decompiled as <c>fp_c3</c>), binding 4, 256 bytes - only slot 0
/// (<c>cParam</c>) is read.
/// </summary>
public sealed class HdrComposeParamsUbo : IUboBlock
{
    public const int ByteSize = 256;

    readonly Std140Block _block = new(ByteSize);

    public string Name => "cContext";
    public int BindingIndex => (int)Profiles.Totk.TotkBindings.HdrComposeParams;

    public static HdrComposeParamsUbo BuildDefault()
    {
        var ubo = new HdrComposeParamsUbo();
        ubo._block.SetSlot(0, new Vector4(1f, 0f, 0f, 0f));
        return ubo;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
