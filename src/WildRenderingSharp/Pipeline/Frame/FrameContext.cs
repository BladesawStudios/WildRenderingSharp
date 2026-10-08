using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>
/// One frame's inputs and the results stages hand to later stages. Setup fills the camera, sun and
/// draw groups; each later stage reads what it needs and records what it produces.
/// </summary>
public sealed class FrameContext(FrameRequest request, RenderTargets targets, ShadowCache shadowCache, GpuTexture? shadowMapOverride)
{
    public FrameRequest Request { get; } = request;
    public RenderTargets Targets { get; } = targets;
    public ShadowCache ShadowCache { get; } = shadowCache;

    /// <summary>A shadow map drawn elsewhere, used instead of drawing one.</summary>
    public GpuTexture? ShadowMapOverride { get; } = shadowMapOverride;

    public Camera Camera => Request.Camera;
    public LightingContext Lighting => Request.Lighting;
    public EnvPalette Palette => Request.Palette;
    public IReadOnlyList<InstanceBatch> Instances { get; } = request.Instances ?? [];
    public SkyPostFx SkyPostFx { get; } = request.SkyPostFx ?? SkyPostFx.Default;
    public CloudPostFx CloudPostFx { get; } = request.CloudPostFx ?? CloudPostFx.Default;
    public SkyBinLut SkyBin { get; } = request.SkyBin ?? SkyBinLut.Empty;

    public CameraData Cam { get; set; }

    /// <summary>The camera with the projection's second row negated.</summary>
    public CameraData FlippedCam { get; set; }

    /// <summary>Whether the driver supports the window origin the game's shaders were written for.</summary>
    public bool GameOrigin { get; set; }

    public Vector3 SunWorld { get; set; }
    public Vector3 SunView { get; set; }
    public Vector3 SunColor { get; set; }
    public Vector3 HemiSky { get; set; }
    public Vector3 HemiGround { get; set; }

    /// <summary>Every group that casts a shadow, including shapes only there for it.</summary>
    public List<ActorDrawGroup> CastingGroups { get; set; } = [];

    /// <summary>Every group that is seen.</summary>
    public List<ActorDrawGroup> Groups { get; set; } = [];

    /// <summary>Groups of shapes that are neither blended nor read the lit scene.</summary>
    public List<ActorDrawGroup> OpaqueGroups { get; set; } = [];

    public bool TerrainDrawn { get; set; }

    public ShadowPass.LightMatrices LightMatrices { get; set; }
    public Vector3 ShadowBoundsLo { get; set; }
    public Vector3 ShadowBoundsHi { get; set; }
    public ScreenSpaceShadowAndAoPass.CascadeParams? Cascades { get; set; }

    public ScreenSpaceShadowAndAoPass.Params ShadowAoParams { get; set; }
    public LightPrePass.Params LightPrePassParams { get; set; }

    /// <summary>The unflipped view-projection in the game's world, which the pass-ID mask and highlight draw with.</summary>
    public Vector4[] MaskViewProj { get; set; } = [];

    public (long Triangles, long Instances) GBufferCounts { get; set; }
    public (long Triangles, long Instances) ShadowCounts { get; set; }

    /// <summary>The exposed, highlight-compressed HDR image the bloom and composite read.</summary>
    public GpuTexture HdrCompressed { get; set; }

    public FrameResult Result => new(Targets.Ldr, Targets.Final, Targets.GBuffer[1], Targets.GBuffer[3], Targets.PreShadow, Targets.PreMisc, Targets.PassId);
}
