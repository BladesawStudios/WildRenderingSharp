using System.Text.Json;

namespace WildRenderingSharp.Rendering;

public sealed class CloudPostFx
{
    public CloudPostFxShared Shared = CloudPostFxShared.Default;
    public CloudPostFxLayer Layer0 = CloudPostFxLayer.Default;
    public CloudPostFxLayer Layer1 = CloudPostFxLayer.Default;

    public static readonly CloudPostFx Default = new();

    internal static CloudPostFx FromJson(JsonElement cloud)
    {
        var result = new CloudPostFx();
        if (cloud.TryGetProperty("cloud", out var shared)) result.Shared = CloudPostFxShared.FromJson(shared);
        if (cloud.TryGetProperty("layer0", out var l0)) result.Layer0 = CloudPostFxLayer.FromJson(l0);
        if (cloud.TryGetProperty("layer1", out var l1)) result.Layer1 = CloudPostFxLayer.FromJson(l1);
        return result;
    }
}
