using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Botw.Ubos;

/// <summary>BotW <c>gsys_scene_material</c> ("SceneMat"), 96 bytes: the scene-wide switches the deferred passes read.</summary>
public sealed class BotwSceneMatUbo : IUboBlock
{
    public const int ByteSize = 96;

    public static class Fields
    {
        public const int ToonLightAdjustForDemo = 0;
        public const int CloudRatio = 16;
        public const int DepthShadowOff = 20;
        public const int ProjShadowOff = 24;
        public const int RainRatio = 28;
        public const int Rainfall = 32;
        public const int Exposure = 36;
        public const int WorldShadowOff = 40;
        public const int BaseLightChangeRatio = 44;
        public const int MainRenderingLight = 48;
        public const int SkyOcclusionOff = 52;
        public const int DepthShadowScale = 56;
        public const int ItemFilterAlpha = 60;
        public const int UiHighlight = 64;
        public const int ProcDiscardScale1 = 68;
        public const int ProcDiscardScale2 = 72;
        public const int ProcDiscardScale3 = 76;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "SceneMat";
    public int BindingIndex => (int)BotwBindings.SceneMaterial;

    public static BotwSceneMatUbo From(float midScale, float highlightScale)
    {
        var scene = new BotwSceneMatUbo();
        var b = scene._block;
        b.SetVectorAt(Fields.ToonLightAdjustForDemo, midScale, midScale, midScale, 0f);
        b.SetFloatAt(Fields.CloudRatio, 1f);
        b.SetFloatAt(Fields.Exposure, 1f);
        b.SetFloatAt(Fields.WorldShadowOff, 1f);
        b.SetFloatAt(Fields.BaseLightChangeRatio, highlightScale);
        b.SetFloatAt(Fields.MainRenderingLight, 1f);
        b.SetFloatAt(Fields.DepthShadowScale, 1f);
        b.SetFloatAt(Fields.ProcDiscardScale1, 0.1f);
        b.SetFloatAt(Fields.ProcDiscardScale2, 10f);
        b.SetFloatAt(Fields.ProcDiscardScale3, 0.25f);
        return scene;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
