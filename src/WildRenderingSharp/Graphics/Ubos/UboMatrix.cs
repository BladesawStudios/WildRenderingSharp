namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>A matrix field of a block: the slot of its first row, and how many rows the shader declares (3 for a mat3x4, 4 for a mat4).</summary>
public readonly record struct UboMatrix(int Slot, int Rows);
