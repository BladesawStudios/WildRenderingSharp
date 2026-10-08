using System.Text.Json;
using AampSharp;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

static class PostFxAamp
{
    static readonly string[] SkyScalars =
    [
        "rayleigh_base_height", "mie_base_height", "mie_scattering_coeff", "mie_symmetrical_prop_rendering",
        "rayleigh_amplifier_rendering", "mie_amplifier_rendering", "render_sun_intensity", "render_sun_size",
        "render_sun_lerp", "scatter_fog_near", "scatter_fog_far", "scatter_fog_density", "scatter_fog_atten", "scatter_fog_horz",
        "adhoc_fog_near", "adhoc_fog_far", "adhoc_fog_atten_grd", "adhoc_fog_atten_sky", "adhoc_fog_atten_minscale_sky",
    ];

    static readonly string[] SkyColors = ["sun_color", "ground_color", "adhoc_fog_color"];

    const uint RayleighScatteringCoeffHash = 3995358905u;

    public static string ToJson(byte[]? sky, byte[]? cloud, byte[]? colorCorrection)
    {
        var result = new Dictionary<string, object?>();
        if (sky is not null) result["sky"] = ReadSky(ParameterIO.FromBinary(sky));
        if (cloud is not null) result["cloud"] = ReadCloud(ParameterIO.FromBinary(cloud));
        if (colorCorrection is not null) result["colorCorrection"] = ReadObject(ParameterIO.FromBinary(colorCorrection).Root.Object("color_correction"));
        return JsonSerializer.Serialize(result);
    }

    static Dictionary<string, object?> ReadSky(ParameterIO file)
    {
        var d = new Dictionary<string, object?>();
        if (file.Root.Object("sky") is not { } sky)
            return d;

        foreach (var name in SkyScalars)
            if (sky[name] is { Type: ParameterType.F32 } p) d[name] = p.AsFloat();

        if (sky[RayleighScatteringCoeffHash] is { Type: ParameterType.Vec3 } v)
        {
            var c = v.AsVector3();
            d["rayleigh_scattering_coeff"] = new[] { c.X, c.Y, c.Z };
        }

        foreach (var name in SkyColors)
            if (sky[name] is { Type: ParameterType.Color } p) d[name] = Components(p.AsColor());
        return d;
    }

    static Dictionary<string, object?> ReadCloud(ParameterIO file)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (key, name) in new[] { ("cloud", "Cloud"), ("layer0", "CloudParam0"), ("layer1", "CloudParam1"), ("layer2", "CloudParam2") })
            if (file.Root.Object(name) is { } obj) d[key] = ReadObject(obj);
        return d;
    }

    static Dictionary<string, object?> ReadObject(ParameterObject? obj)
    {
        var d = new Dictionary<string, object?>();
        if (obj is null)
            return d;

        foreach (var (hash, p) in obj.Parameters)
        {
            if (NameTable.TotK.Find(hash) is not { } name)
                continue;

            d[name] = p.Type switch
            {
                ParameterType.F32 => p.AsFloat(),
                ParameterType.Bool => p.AsBool(),
                ParameterType.Int => p.AsInt(),
                ParameterType.U32 => p.AsUInt(),
                ParameterType.Color => Components(p.AsColor()),
                _ => null,
            };
        }
        return d;
    }

    static float[] Components(System.Numerics.Vector4 c) => [c.X, c.Y, c.Z, c.W];
}
