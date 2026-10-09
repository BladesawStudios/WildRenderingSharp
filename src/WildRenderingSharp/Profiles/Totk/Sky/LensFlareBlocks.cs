using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>The uniform block of <c>agl_flare_filter_flare</c>: Register, with the ghost spacing, the halo and the intensity.</summary>
static class LensFlareBlocks
{
    public static readonly UboSpec Register = new("Register", 24, 192);

    public static Ubo BuildRegister(float ghostSpacing, Vector3 haloTint, float haloRadius, Vector3 intensity)
    {
        var block = new UboWriter(Register);
        block.Set(0, 0, ghostSpacing);
        block.Set(1, haloTint, haloRadius);
        block.SetXyz(3, intensity);
        return block.ToUbo("flare_register");
    }
}
