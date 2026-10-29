using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>The textures the game's pre-shading passes sample, at the units its programs bind them to: the frame's own, and neutral stand-ins for the rest.</summary>
internal sealed class PreShadingTextures(GL gl) : IDisposable
{
    // The programs' sampler bindings.
    const int Albedo = 0, Normal = 1, MaterialId = 2, Emission = 3, LinearDepth = 4, HalfLinearDepth = 5, ShadowCascades = 6,
        DepthBuffer = 7, ScreenVolumeMask = 8, CubeEnvironment = 9, WorldVolumeMask = 11, SkyInscatter = 12, Projection = 13, SkyIslandShadow = 14, Darkness = 15,
        PreShadow = 16, PreMisc = 18, LocalReflection = 25,
        CloudNoise = 26, SkyColor = 27, LightPrePass = 28;

    // Black reads as no world shadow height, no volume, no inscatter, no darkness and no cloud noise; white as a projection that shades nothing.
    readonly uint _black = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
    readonly uint _white = ConstantTextures.Rgba(gl, 1f, 1f, 1f, 1f);
    readonly uint _blackArray = ConstantTextures.RgbaArray(gl, 0f, 0f, 0f, 0f);

    // The screen-space volume mask as one texel, which the field program samples at the pixel's own coordinates: its green channel weighs the low fog (YFog)
    // and its blue the fx fog and the palette's volume tint. The game's pass that fills it is not run, so one share stands for the whole scene.
    readonly uint _volumeMask = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
    float _volumeShare = -1f;

    // Past a cascade's edge the depth is the far plane's, so a lookup there is lit rather than shadowed by the edge texel. The array keeps its own sampling.
    readonly uint _cascadeSampler = CascadeSampler(gl);

    // The screen-space volume mask last bound.
    public uint VolumeMaskHandle { get; private set; }

    // The screen-space volume mask is the one given, or when there is none a single texel holding volumeShare. entranceThickness is cTex_LocalReflection, the depth
    // behind a shrine's doorway the entrance pass hazes over (see EntranceInterior), or 0 when no entrance is drawn.
    public unsafe void Bind(RenderTargets targets, GpuTexture preShadow, uint cubeEnvironment, uint skyInscatter, float volumeShare, uint volumeMask,
        uint entranceThickness)
    {
        if (volumeShare != _volumeShare)
        {
            _volumeShare = volumeShare;
            float* texel = stackalloc float[] { 0f, volumeShare, volumeShare, 0f };
            gl.BindTexture(TextureTarget.Texture2D, _volumeMask);
            gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, texel);
        }

        gl.BindTextureAt(Albedo, targets.GBuffer[1].Handle);
        gl.BindTextureAt(Normal, targets.GBuffer[3].Handle);
        gl.BindTextureAt(MaterialId, targets.GBuffer[0].Handle);
        gl.BindTextureAt(Emission, targets.GBuffer[5].Handle);
        gl.BindTextureAt(LinearDepth, targets.LinearDepth.Handle);
        gl.BindTextureAt(HalfLinearDepth, targets.LinearDepthHalf.Handle);
        gl.BindTextureAt(ShadowCascades, targets.ShadowCascades.Handle, TextureTarget.Texture2DArray);
        gl.BindSampler((uint)ShadowCascades, _cascadeSampler);
        gl.BindTextureAt(PreShadow, preShadow.Handle);
        gl.BindTextureAt(Projection, _white);
        gl.BindTextureAt(PreMisc, _white);
        gl.BindTextureAt(CubeEnvironment, cubeEnvironment, TextureTarget.TextureCubeMap);
        gl.BindTextureAt(SkyColor, _blackArray, TextureTarget.Texture2DArray);
        gl.BindTextureAt(LightPrePass, _blackArray, TextureTarget.Texture2DArray);
        gl.BindTextureAt(SkyInscatter, skyInscatter != 0 ? skyInscatter : _black);
        VolumeMaskHandle = volumeMask != 0 ? volumeMask : _volumeMask;
        gl.BindTextureAt(ScreenVolumeMask, VolumeMaskHandle);
        gl.BindTextureAt(WorldVolumeMask, _blackArray, TextureTarget.Texture2DArray);
        gl.BindTextureAt(DepthBuffer, targets.GBufferDepth.Handle);
        gl.BindTextureAt(LocalReflection, entranceThickness != 0 ? entranceThickness : _black);
        foreach (int unit in new[] { SkyIslandShadow, Darkness, CloudNoise })
            gl.BindTextureAt(unit, _black);
    }

    public void Unbind() => gl.BindSampler((uint)ShadowCascades, 0);

    public void Dispose()
    {
        gl.DeleteSampler(_cascadeSampler);
        gl.DeleteTexture(_black);
        gl.DeleteTexture(_white);
        gl.DeleteTexture(_blackArray);
        gl.DeleteTexture(_volumeMask);
    }

    static unsafe uint CascadeSampler(GL gl)
    {
        uint sampler = gl.GenSampler();
        gl.SamplerParameter(sampler, SamplerParameterI.MinFilter, (int)GLEnum.Linear);
        gl.SamplerParameter(sampler, SamplerParameterI.MagFilter, (int)GLEnum.Linear);
        gl.SamplerParameter(sampler, SamplerParameterI.WrapS, (int)GLEnum.ClampToBorder);
        gl.SamplerParameter(sampler, SamplerParameterI.WrapT, (int)GLEnum.ClampToBorder);
        gl.SamplerParameter(sampler, SamplerParameterI.CompareMode, (int)GLEnum.CompareRefToTexture);
        gl.SamplerParameter(sampler, SamplerParameterI.CompareFunc, (int)GLEnum.Lequal);
        float* border = stackalloc float[] { 1f, 1f, 1f, 1f };
        gl.SamplerParameter(sampler, SamplerParameterF.BorderColor, border);
        return sampler;
    }
}
