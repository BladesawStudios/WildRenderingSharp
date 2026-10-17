namespace WildRenderingSharp.Assets;

/// <summary>A copy of a shape kept somewhere else, which is given back when the shape is.</summary>
internal interface IShapeCopy
{
    void Release(LoadedShape shape);
}
