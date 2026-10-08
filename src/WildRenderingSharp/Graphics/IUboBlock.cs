namespace WildRenderingSharp.Graphics;

/// <summary>A uniform block laid out the way a game's shaders declare it.</summary>
public interface IUboBlock
{
    string Name { get; }
    int BindingIndex { get; }
    byte[] ToByteArray();
}
