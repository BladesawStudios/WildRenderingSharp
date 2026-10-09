namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>A uniform block as its shader declares it: the block's name, the binding it is read at, and its size in bytes.</summary>
public readonly record struct UboSpec(string ShaderName, uint Binding, int ByteSize);
