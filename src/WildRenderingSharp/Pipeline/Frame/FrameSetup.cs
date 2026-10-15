using System.Numerics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>What <see cref="FrameSetupStage"/> derives from the request, for every later stage: the camera, the sun and ambient light, and the draw groups. The sun is where it is in the sky; the light is the direction lighting and shadows use, which does not go below the horizon.</summary>
internal sealed record FrameSetup(
    CameraData Cam, CameraData FlippedCam, bool GameOrigin,
    Vector3 SunWorld, Vector3 SunView, Vector3 LightWorld, Vector3 LightView, Vector3 SunColor, Vector3 HemiSky, Vector3 HemiGround,
    IReadOnlyList<ActorDrawGroup> CastingGroups, IReadOnlyList<ActorDrawGroup> Groups, IReadOnlyList<ActorDrawGroup> OpaqueGroups);
