using Silk.NET.OpenGL;

namespace WildRenderingSharp.Gpu;

/// <summary>The texture bindings of every unit, taken so a pass that binds freely can put back what the passes around it left.</summary>
internal sealed class TextureBindingSnapshot
{
    const int Units = 32;

    static readonly (TextureTarget Target, GetPName Binding)[] Targets =
    [
        (TextureTarget.Texture2D, GetPName.TextureBinding2D),
        (TextureTarget.Texture2DArray, GetPName.TextureBinding2DArray),
        (TextureTarget.Texture3D, GetPName.TextureBinding3D),
        (TextureTarget.TextureCubeMap, GetPName.TextureBindingCubeMap),
    ];

    readonly int[,] _bound = new int[Units, Targets.Length];
    int _active;

    public void Capture(GL gl)
    {
        _active = gl.GetInteger(GetPName.ActiveTexture);
        for (int unit = 0; unit < Units; unit++)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            for (int target = 0; target < Targets.Length; target++)
                _bound[unit, target] = gl.GetInteger(Targets[target].Binding);
        }
        gl.ActiveTexture((TextureUnit)_active);
    }

    public void Restore(GL gl)
    {
        for (int unit = 0; unit < Units; unit++)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            for (int target = 0; target < Targets.Length; target++)
                gl.BindTexture(Targets[target].Target, (uint)_bound[unit, target]);
        }
        gl.ActiveTexture((TextureUnit)_active);
    }
}
