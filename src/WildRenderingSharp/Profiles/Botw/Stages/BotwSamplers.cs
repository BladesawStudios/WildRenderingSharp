namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>The G-buffer attachments the game's passes read, under the names of the samplers they bind them to.</summary>
public static class BotwGBuffer
{
    public const int MaterialId = 0, Albedo = 1, Normal = 3, Emission = 5;
}

/// <summary>The texture units each of the game's passes samples from. The numbering differs between passes, so each has its own.</summary>
public static class BotwSamplers
{
    /// <summary>Units the renderer adds to every pass: the mask's ID texture and the ambient strip.</summary>
    public const int IdTexture = 20, LightAnalyzed = 21;

    public static class ShadowChara
    {
        public const int Albedo = 0, Normal = 1, WorldShadow = 9, ShadowCascade = 10, HalfDepth = 12, ResourceTexture1 = 14;
    }

    public static class LightChara
    {
        public const int Albedo = 0, Normal = 1, Emission = 2, Projection = 3, MaterialId = 5, SkyInscatter = 6,
            Expand = 8, VolumeMask = 9, HalfDepth = 13, ResourceTexture0 = 14, CubeEnvironment = 16;
    }

    public static class Shading
    {
        public const int Albedo = 0, Normal = 1, LinearDepth = 3, Shadow = 5, PreFog = 7, RenderDepth = 8,
            LightPrePass = 13, HalfDepth = 14;
    }
}
