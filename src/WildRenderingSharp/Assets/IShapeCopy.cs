namespace WildRenderingSharp.Assets;

/// <summary>A copy of a shape kept somewhere else, which is given back when the shape is.</summary>
internal interface IShapeCopy
{
    void Release(LoadedShape shape);

    // Makes the shape's own buffers and vertex arrays again from the copy, for a draw that cannot be made from it.
    void Restore(LoadedShape shape);
}
