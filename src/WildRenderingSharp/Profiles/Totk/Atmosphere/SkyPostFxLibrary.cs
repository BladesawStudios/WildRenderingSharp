using System.Numerics;
using System.Text.Json;
using SarcLibrary;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>Loads the sky, cloud and colour-correction post-fx once from <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c>; cached process-wide.</summary>
public static class SkyPostFxLibrary
{
    static (SkyPostFx Sky, CloudPostFx Cloud, ColorCorrectionPostFx ColorCorrection)? _cached;


    static ColorCorrectionPostFx ColorCorrectionFromJson(JsonElement el)
    {
        var cc = new ColorCorrectionPostFx();
        float F(string k, float d) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
        bool B(string k, bool d) => el.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : d;
        Vector3 C(string k, Vector3 d)
        {
            if (!el.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 3)
                return d;
            return new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
        }

        cc.Enable = B("enable", cc.Enable);
        cc.Hue = F("hue", cc.Hue);
        cc.Saturation = F("saturation", cc.Saturation);
        cc.Brightness = F("brightness", cc.Brightness);
        cc.Gamma = F("gamma", cc.Gamma);
        cc.OrderToycamHsb = B("order_toycam_hsb", cc.OrderToycamHsb);
        cc.ToycamEnable = B("toycam_enable", cc.ToycamEnable);
        cc.ToycamOffset1 = C("toycam_offset1", cc.ToycamOffset1);
        cc.ToycamOffset2 = C("toycam_offset2", cc.ToycamOffset2);
        cc.ToycamLevel1 = C("toycam_level1", cc.ToycamLevel1);
        cc.ToycamLevel2 = C("toycam_level2", cc.ToycamLevel2);
        cc.ToycamSaturation1 = F("toycam_saturation1", cc.ToycamSaturation1);
        cc.ToycamSaturation2 = F("toycam_saturation2", cc.ToycamSaturation2);
        cc.ToycamBrightness = F("toycam_brightness", cc.ToycamBrightness);
        cc.ToycamContrast = F("toycam_contrast", cc.ToycamContrast);
        cc.ToycamMulColor = C("toycam_mul_color", cc.ToycamMulColor);
        return cc;
    }

    public static (SkyPostFx Sky, CloudPostFx Cloud, ColorCorrectionPostFx ColorCorrection) LoadFromRomfs(string? romfsRoot)
    {
        if (_cached is { } cached)
            return cached;

        var fallback = (Sky: SkyPostFx.Default, Cloud: CloudPostFx.Default, ColorCorrection: ColorCorrectionPostFx.Default);
        if (string.IsNullOrEmpty(romfsRoot))
            return fallback;

        string genvbPath = Path.Combine(romfsRoot, "Env", "GameScene.Nin_NX_NVN.genvb.zs");
        if (!File.Exists(genvbPath))
        {
            Console.WriteLine($"[SkyPostFxLibrary] no GameScene.Nin_NX_NVN.genvb.zs under '{romfsRoot}' - TotK Sky background falls back to hand-transcribed defaults.");
            return fallback;
        }

        try
        {
            byte[] raw = File.ReadAllBytes(genvbPath);
            byte[] decompressed = TotkCommon.Zstd.IsCompressed(raw) ? TotkCommon.Totk.Zstd.Decompress(raw) : raw;
            var sarc = Sarc.FromBinary(new ArraySegment<byte>(decompressed));

            byte[]? skyBytes = null, cloudBytes = null, ccrBytes = null;
            foreach (var (path, data) in sarc)
            {
                if (path == "postfx/master_field.baglsky") skyBytes = data.ToArray();
                else if (path == "postfx/master_field.baglclwd") cloudBytes = data.ToArray();
                else if (path == "postfx/master_field.baglccr") ccrBytes = data.ToArray();
            }
            if (skyBytes is null) Console.WriteLine("[SkyPostFxLibrary] 'postfx/master_field.baglsky' not found in genvb archive - using defaults for sky.");
            if (cloudBytes is null) Console.WriteLine("[SkyPostFxLibrary] 'postfx/master_field.baglclwd' not found in genvb archive - using defaults for clouds.");

            string json = PostFxAamp.ToJson(skyBytes, cloudBytes, ccrBytes);
            using var doc = JsonDocument.Parse(json);
            var sky = doc.RootElement.TryGetProperty("sky", out var skyEl) ? SkyPostFx.FromJson(skyEl) : SkyPostFx.Default;
            var cloud = doc.RootElement.TryGetProperty("cloud", out var cloudEl) ? CloudPostFx.FromJson(cloudEl) : CloudPostFx.Default;

            var cc = doc.RootElement.TryGetProperty("colorCorrection", out var ccEl)
                ? ColorCorrectionFromJson(ccEl) : ColorCorrectionPostFx.Default;

            Console.WriteLine($"[SkyPostFxLibrary] loaded real sky/cloud postfx from romfs " +
                $"(colour correction: enable={cc.Enable} saturation={cc.Saturation:G4} brightness={cc.Brightness:G4} gamma={cc.Gamma:G4} toycam={cc.ToycamEnable}).");
            _cached = (sky, cloud, cc);
            return _cached.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkyPostFxLibrary] failed to load real postfx from romfs, using defaults: {ex.Message}");
            return fallback;
        }
    }
}
