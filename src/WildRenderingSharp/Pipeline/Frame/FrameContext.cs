using System.Numerics;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Cameras;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>One frame's inputs and the results stages hand to later stages.</summary>
internal sealed class FrameContext(FrameRequest request, RenderTargets targets, ShadowCache shadowCache, GpuTexture? shadowMapOverride)
{
    public FrameRequest Request { get; } = request;
    public RenderTargets Targets { get; } = targets;
    public ShadowCache ShadowCache { get; } = shadowCache;

    public GpuTexture? ShadowMapOverride { get; } = shadowMapOverride;

    public bool SnapshotStages { get; init; }

    public Camera Camera => Request.Camera;
    public LightingContext Lighting => Request.Lighting;
    public IFrameEnvironment Environment => Request.Environment;
    public IReadOnlyList<InstanceBatch> Instances { get; } = request.Instances ?? [];

    public CameraData Cam { get; set; }

    public CameraData FlippedCam { get; set; }

    public bool GameOrigin { get; set; }

    public Vector3 SunWorld { get; set; }
    public Vector3 SunView { get; set; }
    public Vector3 SunColor { get; set; }
    public Vector3 HemiSky { get; set; }
    public Vector3 HemiGround { get; set; }

    public List<ActorDrawGroup> CastingGroups { get; set; } = [];

    public List<ActorDrawGroup> Groups { get; set; } = [];

    public List<ActorDrawGroup> OpaqueGroups { get; set; } = [];

    public bool TerrainDrawn { get; set; }

    // Projection/view matrices for lights
    public ShadowPass.LightMatrices LightMatrices { get; set; }
    public Vector3 ShadowBoundsLo { get; set; }
    public Vector3 ShadowBoundsHi { get; set; }
    public ScreenSpaceShadowAndAoPass.CascadeParams? Cascades { get; set; }

    public ScreenSpaceShadowAndAoPass.Params ShadowAoParams { get; set; }
    public LightPrePass.Params LightPrePassParams { get; set; }

    /// <summary>The scene view-projection, for the mask and overlay shaders.</summary>
    public Matrix4x4 MaskViewProj { get; set; }

    public (long Triangles, long Instances) GBufferCounts { get; set; }
    public (long Triangles, long Instances) ShadowCounts { get; set; }

    public GpuTexture HdrCompressed { get; set; }

    public FrameResult Result => new(Targets.Ldr, Targets.Final, Targets.GBuffer[1], Targets.GBuffer[3], Targets.PreShadow, Targets.PreMisc, Targets.PassId);
}
