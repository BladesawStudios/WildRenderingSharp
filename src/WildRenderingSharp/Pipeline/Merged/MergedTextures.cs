using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>The bindless handles of every shape's textures in a storage buffer, a slot of them for each program a shape draws with, indexed by the unit the shader says.</summary>
internal sealed class MergedTextures : IDisposable
{
    const string DefaultBakeName = "CmnTex_BakeDefault", BakeSampler = "bake0";

    readonly BindlessHandles _handles;
    readonly DefaultBake _defaultBake;
    readonly RegionAllocator _slots = new(MergedLayout.HandleSlots * 1024);
    readonly List<(int Entry, LoadedTexture Own)> _defaultBakeEntries = [];
    ulong _white;
    float _strength = float.NaN;

    public MergedTextures(GL gl, BindlessHandles handles, DefaultBake defaultBake)
    {
        (_handles, _defaultBake) = (handles, defaultBake);
        Buffer = new GrowingBuffer(gl, 8, MergedLayout.HandleSlots * 1024);
    }

    public GrowingBuffer Buffer { get; }

    // The first handle of a new slot holding the shape's samplers; a unit the shape binds nothing to reads white, as a missing texture does.
    public int Add(IReadOnlyList<ShapeSampler> samplers, LoadedTexture white)
    {
        int at = _slots.Allocate(MergedLayout.HandleSlots);
        if (at < 0)
        {
            _slots.Grow(_slots.Capacity * 2);
            at = _slots.Allocate(MergedLayout.HandleSlots);
        }
        Buffer.EnsureCapacity(_slots.Capacity);

        _white = _handles.Of(white);
        var table = new ulong[MergedLayout.HandleSlots];
        Array.Fill(table, _white);
        foreach (var (unit, key, texture) in samplers)
        {
            if ((uint)unit >= MergedLayout.HandleSlots)
                continue;
            table[unit] = _handles.Of(_defaultBake.Substitute(key, texture));
            if (key == BakeSampler && texture.Name == DefaultBakeName)
                _defaultBakeEntries.Add((at + unit, texture));
        }
        Buffer.Write<ulong>(at, table);
        return at;
    }

    // Points the entries that hold the default baked lighting at whatever the viewer's strength makes of it now.
    public void Refresh()
    {
        if (_defaultBake.Strength == _strength)
            return;
        _strength = _defaultBake.Strength;
        foreach (var (entry, own) in _defaultBakeEntries)
            Buffer.Write<ulong>(entry, [_handles.Of(_defaultBake.Substitute(BakeSampler, own))]);
    }

    public void Remove(int at)
    {
        _defaultBakeEntries.RemoveAll(e => e.Entry >= at && e.Entry < at + MergedLayout.HandleSlots);
        _slots.Free(at, MergedLayout.HandleSlots);
    }

    public void Dispose() => Buffer.Dispose();
}
