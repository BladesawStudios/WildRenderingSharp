using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>The bindless handles a renderer has taken, by the texture each came from, made resident on the render thread and let go of before their texture is deleted or the renderer dropped.</summary>
/// <remarks>
/// A handle is resident in the context that made it so, and only that one may sample it, so a handle taken while a model loads on the loader's context waits here until the
/// render thread makes it resident. A resident handle keeps its texture on the card for as long as it is resident, whatever happens to the texture's name. A map's worth of
/// them, left resident when the renderer that took them went away, was video memory nothing could take back and the viewer's own scene was then left to fit in what
/// remained, which it did by paging.
/// </remarks>
internal sealed class TextureResidency : IResidency
{
    readonly Dictionary<uint, List<ulong>> _handles = [];

    // Taken and not yet resident, resident, and no longer wanted while resident.
    readonly HashSet<ulong> _waiting = [], _resident = [];
    readonly List<ulong> _retired = [];

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

    // Makes the waiting handles resident and the retired ones not, in the current context, which is the one that draws.
    public void MakeResident(GL gl)
    {
        ulong[] wanted, unwanted;
        lock (_handles)
        {
            if (_waiting.Count == 0 && _retired.Count == 0)
                return;
            wanted = [.. _waiting];
            unwanted = [.. _retired];
            _waiting.Clear();
            _retired.Clear();
            _resident.UnionWith(wanted);
            _resident.ExceptWith(unwanted);
        }
        var bindless = MergedSupport.Bindless(gl);
        foreach (ulong handle in wanted)
            bindless.MakeTextureHandleResident(handle);
        foreach (ulong handle in unwanted)
            bindless.MakeTextureHandleNonResident(handle);
    }

    // Lets go of the handles taken from a texture, ahead of the texture being deleted; the render thread makes them non-resident at its next MakeResident.
    public void Release(GL gl, uint texture)
    {
        lock (_handles)
        {
            if (!_handles.Remove(texture, out var held))
                return;
            foreach (ulong handle in held)
                if (!_waiting.Remove(handle) && _resident.Contains(handle))
                    _retired.Add(handle);
        }
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
            _retired.Clear();
        }
        if (held.Length == 0 || !MergedSupport.Available(gl))
            return;
        var bindless = MergedSupport.Bindless(gl);
        foreach (ulong handle in held)
            bindless.MakeTextureHandleNonResident(handle);
    }
}
