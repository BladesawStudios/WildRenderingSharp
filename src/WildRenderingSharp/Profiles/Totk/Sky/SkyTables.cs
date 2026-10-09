using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using static WildRenderingSharp.Profiles.Totk.Sky.SkyPrecomputePass;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>The textures the sky precompute chain solves into: the 2D tables, the 3D scattering tables, and the baked 2D inscatter the per-frame sky samples.</summary>
sealed class SkyTables : IDisposable
{
    readonly GL _gl;

    public SkyTables(GL gl)
    {
        _gl = gl;
        Transmittance = CreateTable2D(gl, TransmittanceW, TransmittanceH);
        DeltaE = CreateTable2D(gl, IrradianceW, IrradianceH);
        Irradiance = CreateTable2D(gl, IrradianceW, IrradianceH);
        BakedInscatter = CreateTable2D(gl, BakedInscatterW, BakedInscatterH);
        DeltaSR = CreateTable3D(gl, InscatterW, InscatterH, InscatterD);
        DeltaSM = CreateTable3D(gl, InscatterW, InscatterH, InscatterD);
        DeltaJ = CreateTable3D(gl, InscatterW, InscatterH, InscatterD);
        Inscatter = CreateTable3D(gl, InscatterW, InscatterH, InscatterD);
    }

    public uint Transmittance { get; }
    public uint DeltaE { get; }
    public uint Irradiance { get; }
    public uint DeltaSR { get; }
    public uint DeltaSM { get; }
    public uint DeltaJ { get; }
    public uint Inscatter { get; }

    // The table the spectral calibration replaces with a scaled copy.
    public uint BakedInscatter { get; set; }

    // 16F as in the game's allocation, since transmittance spans orders of magnitude along a grazing ray; clamped, since wrapping an edge
    // would fold a grazing ray onto a zenith ray.
    public static unsafe uint CreateTable2D(GL gl, int width, int height)
    {
        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.ClampToEdge);
        return texture;
    }

    static unsafe uint CreateTable3D(GL gl, int width, int height, int depth)
    {
        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture3D, texture);
        gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, (uint)depth, 0, PixelFormat.Rgba, PixelType.Float, null);
        gl.SetSampling(TextureTarget.Texture3D, GLEnum.Linear, GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        return texture;
    }

    public void Dispose()
    {
        foreach (uint texture in new[] { Transmittance, DeltaE, Irradiance, DeltaSR, DeltaSM, DeltaJ, Inscatter, BakedInscatter })
            _gl.DeleteTexture(texture);
    }
}
