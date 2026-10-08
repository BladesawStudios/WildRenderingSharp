
namespace WildRenderingSharp.Rendering;

/// <summary>What fills the pixels no placed actor covers; see <see cref="Pipeline.BackgroundPass"/> for the per-mode rendering.</summary>
public enum BackgroundMode
{
    /// <summary>A flat colour (<see cref="LightingContext.BackgroundColor"/>), the closest thing to a studio backdrop.</summary>
    Color,
    /// <summary>Alpha 0: nothing behind the model. Carried through to file export (see <see cref="Pipeline.PresentPass"/>'s <c>alphaSource</c>).</summary>
    Transparent,
    /// <summary>The ray-marched Rayleigh and Mie sky, parameterised by the current palette's <c>SkyRParam_*</c> and <c>SkySunColor</c> fields; see <see cref="Pipeline.BackgroundPass"/>.</summary>
    TotkSky,
}
