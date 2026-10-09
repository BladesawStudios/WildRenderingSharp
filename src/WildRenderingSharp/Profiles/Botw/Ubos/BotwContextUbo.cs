using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Botw.Ubos;

/// <summary>BotW <c>gsys_context</c>, 2320 bytes. The camera slots sit where TotK's do; the rest is zero until a shader that reads it is drawn.</summary>
public sealed class BotwContextUbo : IUboBlock
{
    public const int ByteSize = 2320;

    public static class Slots
    {
        public const int View = 0;
        public const int ViewProj = 3;
        public const int Proj = 7;
        public const int ViewInv = 11;
        public const int CameraParam0 = 14;
        public const int CameraParam1 = 15;
        public const int CameraParam2 = 16;
        public const int CameraParam3 = 17;
        public const int Resolution = 18;
        public const int Cascades = 42;
        public const int CascadeSplits = 58;
    }

    readonly Std140Block _block = new(ByteSize);

    public string Name => "Context";
    public int BindingIndex => (int)BotwBindings.Camera;

    public static BotwContextUbo ForCamera(
        ReadOnlySpan<Vector4> view, ReadOnlySpan<Vector4> viewProj, ReadOnlySpan<Vector4> proj, ReadOnlySpan<Vector4> viewInv,
        float aspect, float tanHalfFovY, float near, float far, Vector2 texelSize)
    {
        var ctx = new BotwContextUbo();
        ctx._block.WriteRows(Slots.View, view);
        ctx._block.WriteRows(Slots.ViewProj, viewProj);
        ctx._block.WriteRows(Slots.Proj, proj);
        ctx._block.WriteRows(Slots.ViewInv, viewInv);
        ctx._block.SetSlot(Slots.CameraParam0, near, far, near / far, 1f - near / far);
        ctx._block.SetSlot(Slots.CameraParam1, 1f / (far - near), near / (far - near), aspect, 1f / aspect);
        ctx._block.SetSlot(Slots.CameraParam2, far - near, 1f / (1f - near / far), 0f, 0f);
        ctx._block.SetSlot(Slots.CameraParam3, aspect * tanHalfFovY, tanHalfFovY, 2f * MathF.Atan(tanHalfFovY), 0f);
        ctx._block.SetSlot(Slots.Resolution, 1f / texelSize.X, 1f / texelSize.Y, texelSize.X, texelSize.Y);
        return ctx;
    }

    /// <summary>A copy that carries the shadow cascades the pre-shading passes read: each cascade's view-to-shadow matrix (four rows, texture space), and the depths where the next cascade takes over.</summary>
    public BotwContextUbo WithCascade(ReadOnlySpan<Vector4> viewToShadow, float firstSplit, float secondSplit)
    {
        var copy = new BotwContextUbo();
        for (int slot = 0; slot < _block.SizeBytes / 16; slot++)
            copy._block.SetSlot(slot, _block.GetSlot(slot));
        copy._block.WriteRows(Slots.Cascades, viewToShadow);
        copy._block.SetSlot(Slots.CascadeSplits, firstSplit, secondSplit, secondSplit, secondSplit);
        return copy;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
