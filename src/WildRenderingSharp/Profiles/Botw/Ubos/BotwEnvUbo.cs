using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Botw.Ubos;

/// <summary>BotW <c>gsys_environment</c> ("Env"), 992 bytes. Its first slots are the engine's light and fog groups, laid out as in TotK.</summary>
public sealed class BotwEnvUbo : IUboBlock
{
    public const int ByteSize = 992;

    public static class Slots
    {
        public const int Ambient = 0;
        public const int HemiSky = 1;
        public const int HemiGround = 2;
        public const int HemiDir = 3;
        public const int LightDir0 = 4;
        public const int LightColor0 = 5;
        public const int LightSpecColor0 = 6;
        public const int LightDir1 = 7;
        public const int LightColor1 = 8;
        public const int LightSpecColor1 = 9;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "Env";
    public int BindingIndex => (int)BotwBindings.Environment;

    /// <param name="sunDirView">Toward the sun, in view space; the shaders take the direction the light travels.</param>
    public static BotwEnvUbo From(Vector3 sunDirView, Vector3 sunColor, Vector3 hemiSky, Vector3 hemiGround)
    {
        var env = new BotwEnvUbo();
        var b = env._block;
        b.SetSlot(Slots.Ambient, 0.10f, 0.11f, 0.13f, 1f);
        b.SetVec3(Slots.HemiSky, hemiSky, 1f);
        b.SetVec3(Slots.HemiGround, hemiGround, 1f);
        b.SetVec3(Slots.HemiDir, Vector3.UnitY, 0f);
        b.SetVec3(Slots.LightDir0, -sunDirView, 1f);
        b.SetVec3(Slots.LightColor0, sunColor, 1f);
        b.SetVec3(Slots.LightSpecColor0, sunColor, 1f);
        b.SetSlot(Slots.LightDir1, 0f, -1f, 0f, 0f);
        b.SetSlot(Slots.LightColor1, 0f, 0f, 0f, 1f);
        b.SetSlot(Slots.LightSpecColor1, 0f, 0f, 0f, 1f);
        return env;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
