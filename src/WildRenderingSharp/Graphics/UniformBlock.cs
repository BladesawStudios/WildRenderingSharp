
namespace WildRenderingSharp.Graphics;

/// <summary>
/// One uniform buffer a profile wants bound: its bytes, the binding point the game's shaders read it at, and the key its GL buffer
/// is kept under between frames. A block with no data stands for the shared zeroed buffer, for shaders that declare a block nothing
/// fills.
/// </summary>
public readonly record struct UniformBlock(string Key, uint Binding, byte[]? Data)
{
    public static UniformBlock Zeroed(uint binding) => new("", binding, null);

    public static UniformBlock From(string key, IUboBlock block) => new(key, (uint)block.BindingIndex, block.ToByteArray());
}
