using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

public sealed class CloudPostFx
{
    public CloudPostFxShared Shared = CloudPostFxShared.Default;

    /// <summary><c>CloudParam0</c> to <c>CloudParam2</c>: the baseline of the three cloud layers.</summary>
    public CloudPostFxLayer[] Layers = [CloudPostFxLayer.Default, CloudPostFxLayer.Default, CloudPostFxLayer.Default];

    public CloudPostFxLayer Layer0 => Layers[0];
    public CloudPostFxLayer Layer1 => Layers[1];

    public static readonly CloudPostFx Default = new();

    internal static CloudPostFx FromJson(JsonElement cloud)
    {
        var result = new CloudPostFx();
        if (cloud.TryGetProperty("cloud", out var shared)) result.Shared = CloudPostFxShared.FromJson(shared);
        for (int i = 0; i < result.Layers.Length; i++)
            if (cloud.TryGetProperty($"layer{i}", out var layer))
                result.Layers[i] = CloudPostFxLayer.FromJson(layer);
        return result;
    }
}
