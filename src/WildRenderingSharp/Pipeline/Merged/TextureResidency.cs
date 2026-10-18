using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>The bindless handles a renderer has made resident, by the texture each came from, so that a texture is never deleted, or a renderer dropped, with its handles still resident.</summary>
/// <remarks>
/// A resident handle keeps its texture on the card for as long as it is resident, whatever happens to the texture's name. A map's worth of them, left resident when the renderer
/// that took them went away, was video memory nothing could take back and the viewer's own scene was then left to fit in what remained, which it did by paging.
/// </remarks>
internal sealed class TextureResidency : IResidency
{
    readonly Dictionary<uint, List<ulong>> _handles = [];

    // Notes a handle made resident, taken from the texture of that name.
    public void Add(uint texture, ulong handle)
    {
        lock (_handles)
        {
            if (!_handles.TryGetValue(texture, out var list))
                _handles[texture] = list = [];
            list.Add(handle);
        }
    }

    // Makes the handles taken from a texture non-resident, ahead of the texture being deleted.
    public void Release(GL gl, uint texture)
    {
        List<ulong>? held;
        lock (_handles)
        {
            if (!_handles.Remove(texture, out held))
                return;
        }
        if (!MergedSupport.Available(gl))
            return;
        var bindless = MergedSupport.Bindless(gl);
        foreach (ulong handle in held)
            bindless.MakeTextureHandleNonResident(handle);
    }

    // Makes every handle non-resident, for a renderer that is going away.
    public void ReleaseAll(GL gl)
    {
        uint[] textures;
        lock (_handles)
            textures = [.. _handles.Keys];
        foreach (uint texture in textures)
            Release(gl, texture);
    }
}
