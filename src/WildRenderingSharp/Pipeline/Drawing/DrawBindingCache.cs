using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>Remembers what is bound while a pass draws, so a draw can skip binds the previous draw already made.</summary>
sealed class DrawBindingCache
{
    const int TrackedUnits = 96;

    public enum Slot { Program, Material, Vao, BakeTable }

    readonly uint[] _slots = new uint[4];
    readonly uint[] _textures = new uint[TrackedUnits];
    InstanceBatch? _uniformBatch;
    int _activeUnit = -1;

    public bool Enabled { get; private set; }

    public void Begin()
    {
        Invalidate();
        Enabled = true;
    }

    public void End()
    {
        Enabled = false;
        Invalidate();
    }

    public void Invalidate()
    {
        Array.Clear(_slots);
        Array.Clear(_textures);
        _uniformBatch = null;
        _activeUnit = -1;
    }

    // True when the slot must be bound to the value, which it is then remembered as holding.
    public bool Needs(Slot slot, uint value)
    {
        if (Enabled && _slots[(int)slot] == value)
            return false;
        _slots[(int)slot] = value;
        return true;
    }

    // The instancing uniforms live in the program, so a new program has none of a batch's values yet.
    public bool NeedsBatchUniforms(InstanceBatch batch)
    {
        if (Enabled && ReferenceEquals(_uniformBatch, batch))
            return false;
        _uniformBatch = batch;
        return true;
    }

    public void ForgetBatchUniforms() => _uniformBatch = null;

    public void BindTexture(GL gl, int unit, TextureTarget target, uint handle)
    {
        bool tracked = Enabled && (uint)unit < (uint)TrackedUnits;
        if (tracked && _textures[unit] == handle)
            return;
        if (!Enabled || _activeUnit != unit)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _activeUnit = unit;
        }
        gl.BindTexture(target, handle);
        if (tracked)
            _textures[unit] = handle;
    }
}
