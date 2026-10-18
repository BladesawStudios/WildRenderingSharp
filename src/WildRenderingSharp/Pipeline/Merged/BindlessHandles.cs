using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>The 64-bit handle a shader samples a texture by, taken once per texture and made resident for good.</summary>
/// <remarks>
/// A handle is kept with the texture it was taken from, not under the texture's GL name, which is handed out again once a texture is deleted. A texture the
/// host has since swapped for another has a new name, which is what makes its handle be taken again.
/// </remarks>
internal sealed class BindlessHandles(GL gl, TextureResidency residency)
{
    sealed class Taken(uint texture, ulong handle)
    {
        public uint Texture { get; } = texture;

        public ulong Handle { get; } = handle;
    }

    readonly ConditionalWeakTable<LoadedTexture, Taken> _taken = [];

    public ulong Of(LoadedTexture texture)
    {
        if (_taken.TryGetValue(texture, out var held) && held.Texture == texture.Handle)
            return held.Handle;

        var bindless = MergedSupport.Bindless(gl);
        ulong handle = bindless.GetTextureHandle(texture.Handle);
        residency.Add(texture.Handle, handle);
        _taken.AddOrUpdate(texture, new Taken(texture.Handle, handle));
        return handle;
    }
}
