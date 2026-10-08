using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK <c>gsys_material</c> ("Mat"), bound at binding 8 across every draw (G-buffer, z-only,
/// forward, and every deferred resolve pass's own material). Unlike
/// <see cref="ContextUbo"/>/<see cref="EnvUbo"/>/<see cref="SceneMatUbo"/>, this one has no
/// single fixed field layout - each shading model declares its own <c>gsys_material</c> block
/// (different size, different named uniforms) via its own BFSHA reflection, and a material's
/// real values are already resolved to raw bytes offline by
/// <c>ShaderLibrary.CompileTool.BuildMaterialUbo</c> (shader defaults overlaid with the
/// material's own <c>ShaderParams</c>, joined by name) into one <c>&lt;Material&gt;
/// .gsys_material.bin</c> file per material. So this class is intentionally a thin, documented
/// raw-byte wrapper rather than a fixed-field struct like its siblings - pretending a universal
/// layout exists here would be fiction.
/// </summary>
public sealed class MaterialUbo : IUboBlock
{
    readonly byte[] _data;

    public string Name { get; }
    public int BindingIndex => (int)TotkBindings.Material;
    public int SizeBytes => _data.Length;

    public MaterialUbo(string materialName, byte[] data)
    {
        Name = materialName;
        _data = data;
    }

    public static MaterialUbo LoadFromFile(string materialName, string gsysMaterialBinPath) =>
        new(materialName, File.ReadAllBytes(gsysMaterialBinPath));

    public void WriteTo(Span<byte> destination) => _data.CopyTo(destination);
    public byte[] ToByteArray() => (byte[])_data.Clone();
}
