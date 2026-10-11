using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Targets;

/// <summary>Copies of frame textures kept for looking at the frame between passes.</summary>
sealed class FrameSnapshots(GL gl, TargetTextureFactory textures)
{
    public const int Slots = 4;

    readonly GpuTexture?[] _stages = new GpuTexture?[Slots];
    GpuTexture? _layerCopy;
    GpuTexture? _underAlbedo, _underNormal, _underDepth;

    // Drops the copies, whose textures the factory has just released.
    public void Reset()
    {
        Array.Clear(_stages);
        _layerCopy = null;
        _underAlbedo = _underNormal = _underDepth = null;
    }

    // Copies the source as it stands into the slot.
    public void Capture(int slot, GpuTexture source)
    {
        var copy = _stages[slot] ??= textures.Color(source.Width, source.Height, InternalFormat.Rgba16f);
        gl.CopyImageSubData(source.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            copy.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)source.Width, (uint)source.Height, 1);
    }

    public GpuTexture? Stage(int slot) => _stages[slot];

    // Full-size copies of the albedo, normal and depth under the terrain, made when the terrain draws over what is already there.
    public (GpuTexture Albedo, GpuTexture Normal, GpuTexture Depth) TerrainUnderCopies(int width, int height)
    {
        _underAlbedo ??= textures.Color(width, height, InternalFormat.Rgba8, repeat: false, filterNearest: true);
        _underNormal ??= textures.Color(width, height, InternalFormat.Rgba8, repeat: false, filterNearest: true);
        _underDepth ??= textures.Color(width, height, InternalFormat.R32f, repeat: false);
        return (_underAlbedo.Value, _underNormal.Value, _underDepth.Value);
    }

    // One layer of a texture array as a plain texture, overwritten by the next call.
    public GpuTexture LayerCopy(GpuTexture array, int layer)
    {
        var copy = _layerCopy ??= textures.Color(array.Width, array.Height, InternalFormat.Rgba16f);
        gl.CopyImageSubData(array.Handle, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, layer,
            copy.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)array.Width, (uint)array.Height, 1);
        return copy;
    }
}
