using System.Numerics;
using System.Text.Json;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

/// <summary>Loads the sky, cloud and colour-correction post-fx from <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c>.</summary>
public static class SkyPostFxLibrary
{
    const string Archive = "Env/GameScene.Nin_NX_NVN.genvb.zs";


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

    public static (SkyPostFx Sky, CloudPostFx Cloud, ColorCorrectionPostFx ColorCorrection) Load(IRomAccess? rom)
    {
        var fallback = (Sky: SkyPostFx.Default, Cloud: CloudPostFx.Default, ColorCorrection: ColorCorrectionPostFx.Default);
        if (rom is null)
            return fallback;
        if (!rom.Exists(Archive))
        {
            Console.WriteLine($"[SkyPostFxLibrary] no {Archive} - TotK Sky background falls back to hand-transcribed defaults.");
            return fallback;
        }

        try
        {
            byte[]? skyBytes = ReadEntry(rom, "master_field.baglsky");
            byte[]? cloudBytes = ReadEntry(rom, "master_field.baglclwd");
            byte[]? ccrBytes = ReadEntry(rom, "master_field.baglccr");
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
            return (sky, cloud, cc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkyPostFxLibrary] failed to load real postfx from romfs, using defaults: {ex.Message}");
            return fallback;
        }
    }

    static byte[]? ReadEntry(IRomAccess rom, string name)
    {
        string path = $"{Archive}//postfx/{name}";
        return rom.Exists(path) ? rom.ReadAllBytesNested(path).ToArray() : null;
    }
}
