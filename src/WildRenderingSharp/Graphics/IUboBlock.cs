namespace WildRenderingSharp.Graphics;

/// <summary>
/// Defines the contract for any Uniform Buffer Object block (e.g. Context, Env, SceneMat, Material).
/// </summary>
public interface IUboBlock
{
    string Name { get; }
    int BindingIndex { get; }
    int SizeBytes { get; }
    void WriteTo(Span<byte> destination);
    byte[] ToByteArray();
}
