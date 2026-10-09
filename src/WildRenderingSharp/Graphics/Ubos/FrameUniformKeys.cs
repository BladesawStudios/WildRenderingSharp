namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>Keys the frame's uniform buffers are kept under, so a pass can rebind one by name.</summary>
public static class FrameUniformKeys
{
    public const string SceneCamera = "ctx_true";
    public const string GBufferCamera = "ctx_gbuffer";
    public const string LightCamera = "ctx_light";
    public const string TerrainCamera = "ctx_terrain";
    public const string TerrainLightCamera = "ctx_light_terrain";
    public const string Environment = "env";
    public const string SceneMaterial = "scenemat";
    public const string Bones = "bones";
    public const string ShapeMatrix = "shpmtx";
}
