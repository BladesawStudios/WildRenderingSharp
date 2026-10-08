namespace WildRenderingSharp.Rendering;

/// <summary>What fills the pixels no placed actor covers.</summary>
public enum BackgroundMode
{
    /// <summary>A flat colour, <see cref="LightingContext.BackgroundColor"/>.</summary>
    Color,
    /// <summary>Alpha 0, carried through to file export.</summary>
    Transparent,
    /// <summary>The sky the active profile draws from its environment.</summary>
    Sky,
}
