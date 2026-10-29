using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>The bindless handles a renderer has taken, by the texture each came from, made resident on the render thread and let go of before their texture is deleted or the renderer dropped.</summary>
/// <remarks>
/// A handle is resident in the context that made it so, and only that one may sample it, so a handle taken while a model loads on the loader's context waits here until the
/// render thread makes it resident. A resident handle keeps its texture on the card for as long as it is resident, whatever happens to the texture's name. A map's worth of
/// them, left resident when the renderer that took them went away, was video memory nothing could take back and the viewer's own scene was then left to fit in what
/// remained, which it did by paging. A texture's handles are made non-resident and forgotten the moment it is let go of, before it is deleted, because the driver hands a
/// deleted texture's handle values out again: a handle still counted as resident would keep a new texture's from being made resident, and a merged draw
/// sampling an unresident handle ends the process.
/// </remarks>
internal sealed class TextureResidency : IResidency
{
    readonly Dictionary<uint, List<ulong>> _handles = [];

    // Taken and not yet resident, and resident.
    readonly HashSet<ulong> _waiting = [], _resident = [];

    // Notes a handle taken from the texture of that name, which is made resident by the next MakeResident.
    public void Add(uint texture, ulong handle)
    {
        lock (_handles)
        {
            if (!_handles.TryGetValue(texture, out var list))
                _handles[texture] = list = [];
            list.Add(handle);
            if (!_resident.Contains(handle))
                _waiting.Add(handle);
        }
    }

    // Makes the waiting handles resident, in the current context, which is the one that draws.
    public void MakeResident(GL gl)
    {
        ulong[] wanted;
        lock (_handles)
        {
            if (_waiting.Count == 0)
                return;
            wanted = [.. _waiting];
            _waiting.Clear();
            _resident.UnionWith(wanted);
        }
        var bindless = MergedSupport.Bindless(gl);
        foreach (ulong handle in wanted)
            bindless.MakeTextureHandleResident(handle);
    }

    // Lets go of the handles taken from a texture, just before the texture is deleted, on the render thread that made them resident.
    public void Release(GL gl, uint texture)
    {
        List<ulong> resident = [];
        lock (_handles)
        {
            if (!_handles.Remove(texture, out var held))
                return;
            foreach (ulong handle in held)
            {
                _waiting.Remove(handle);
                if (_resident.Remove(handle))
                    resident.Add(handle);
            }
        }
        if (resident.Count == 0)
            return;
        var bindless = MergedSupport.Bindless(gl);
        foreach (ulong handle in resident)
            bindless.MakeTextureHandleNonResident(handle);
    }

    // Makes every handle non-resident, for a renderer that is going away, on its render thread.
    public void ReleaseAll(GL gl)
    {
        ulong[] held;
        lock (_handles)
        {
            held = [.. _resident];
            _handles.Clear();
            _waiting.Clear();
            _resident.Clear();
        }
        if (held.Length == 0 || !MergedSupport.Available(gl))
            return;
        var bindless = MergedSupport.Bindless(gl);
        foreach (ulong handle in held)
            bindless.MakeTextureHandleNonResident(handle);
    }
}
