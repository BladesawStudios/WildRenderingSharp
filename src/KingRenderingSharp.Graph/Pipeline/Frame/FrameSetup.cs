using System.Numerics;
using KingRenderingSharp.Content.Graphics.Data;
using KingRenderingSharp.Graph.Pipeline.Drawing;

namespace KingRenderingSharp.Graph.Pipeline.Frame;

/// <summary>What <see cref="FrameSetupStage"/> derives from the request, for every later stage: the camera, the sun and ambient light, and the draw groups.</summary>
internal sealed record FrameSetup(
    CameraData Cam, CameraData FlippedCam, bool GameOrigin,
    Vector3 SunWorld, Vector3 SunView, Vector3 SunColor, Vector3 HemiSky, Vector3 HemiGround,
    IReadOnlyList<ActorDrawGroup> CastingGroups, IReadOnlyList<ActorDrawGroup> Groups, IReadOnlyList<ActorDrawGroup> OpaqueGroups);
