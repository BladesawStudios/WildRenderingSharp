using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary><c>cTex_CubeEnvMap</c>: sky above the horizon and ground below, standing in for the probe of the scene the game reflects. Up is +Y, the game's.</summary>
sealed class CubeEnvironment : IDisposable
{
    // The cube's edge in texels at its finest level; the field programs read down to level 3, by roughness.
    const int Size = 16;

    readonly GL _gl;
    Vector3 _sky = new(float.NaN), _ground = new(float.NaN);

    public CubeEnvironment(GL gl)
    {
        _gl = gl;
        Handle = gl.GenTexture();
        Set(new Vector3(0.5f), new Vector3(0.2f));
    }

    public uint Handle { get; }

    // Uploads only when the colours changed.
    public unsafe void Set(Vector3 sky, Vector3 ground)
    {
        if (_sky == sky && _ground == ground)
            return;
        (_sky, _ground) = (sky, ground);

        int levels = 1;
        for (int n = Size; n > 1; n >>= 1)
            levels++;

        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);
        for (int level = 0; level < levels; level++)
        {
            int n = Math.Max(1, Size >> level);
            for (int face = 0; face < 6; face++)
            {
                float[] texels = FaceTexels(face, n);
                fixed (float* ptr = texels)
                    _gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, level, InternalFormat.Rgba16f, (uint)n, (uint)n, 0, PixelFormat.Rgba, PixelType.Float, ptr);
            }
        }
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, levels - 1);
        _gl.BindTexture(TextureTarget.TextureCubeMap, 0);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);

    // A smoothstep from ground to sky across a band around the horizon.
    float[] FaceTexels(int face, int n)
    {
        var texels = new float[n * n * 4];
        for (int t = 0; t < n; t++)
        {
            for (int s = 0; s < n; s++)
            {
                var direction = Vector3.Normalize(FaceDirection(face, 2f * (s + 0.5f) / n - 1f, 2f * (t + 0.5f) / n - 1f));
                float up = Math.Clamp((direction.Y + 0.15f) / 0.3f, 0f, 1f);
                up = up * up * (3f - 2f * up);
                var color = Vector3.Lerp(_ground, _sky, up);
                int at = (t * n + s) * 4;
                (texels[at], texels[at + 1], texels[at + 2], texels[at + 3]) = (color.X, color.Y, color.Z, 1f);
            }
        }
        return texels;
    }

    static Vector3 FaceDirection(int face, float u, float v) => face switch
    {
        0 => new Vector3(1f, -v, -u),
        1 => new Vector3(-1f, -v, u),
        2 => new Vector3(u, 1f, v),
        3 => new Vector3(u, -1f, -v),
        4 => new Vector3(u, -v, 1f),
        _ => new Vector3(-u, -v, -1f),
    };
}
