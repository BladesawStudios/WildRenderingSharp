using WildRenderingSharp.Assets.Materials;

namespace WildRenderingSharp.Assets;

/// <summary>A shape's material with some of its float values changed, for the length of one draw.</summary>
/// <remarks>
/// <see cref="Draw"/> hands a change a copy of what the shape's block shows, which names a value by its parameter and the float within it, reads it and sets it.
/// The block shows the copy while the draw runs and what it showed before once the draw is done. A change that finds a parameter missing says so by returning
/// false, and then nothing is shown or drawn.
/// </remarks>
internal sealed class MaterialOverride
{
    readonly MaterialParamLayout _layout;
    readonly byte[] _bytes;

    MaterialOverride(MaterialParamLayout layout, byte[] bytes)
    {
        _layout = layout;
        _bytes = bytes;
    }

    public bool TryGet(string parameter, out float value, int component = 0)
    {
        value = 0f;
        if (At(parameter, component) is not { } at)
            return false;
        value = BitConverter.ToSingle(_bytes, at);
        return true;
    }

    public bool TrySet(string parameter, float value, int component = 0)
    {
        if (At(parameter, component) is not { } at)
            return false;
        BitConverter.TryWriteBytes(_bytes.AsSpan(at, 4), value);
        return true;
    }

    // Whether the shape was drawn: false when it has no parameter layout or the change found a parameter missing.
    public static bool Draw(LoadedShape shape, Func<MaterialOverride, bool> change, Action draw)
    {
        if (shape.MaterialParams is not { } layout)
            return false;
        byte[] shown = shape.MaterialBlock.Shown.ToArray();
        var material = new MaterialOverride(layout, (byte[])shown.Clone());
        if (!change(material))
            return false;
        shape.MaterialBlock.Show(material._bytes);
        try
        {
            draw();
        }
        finally
        {
            shape.MaterialBlock.Show(shown);
        }
        return true;
    }

    int? At(string parameter, int component) =>
        _layout.TryGetOffset(parameter, out int offset) && offset + (component + 1) * 4 <= _bytes.Length ? offset + component * 4 : null;
}
