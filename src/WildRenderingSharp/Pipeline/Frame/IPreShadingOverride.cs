namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>A game's own pre-shading passes, which replace the approximated screen-space buffers wherever they draw.</summary>
internal interface IPreShadingOverride
{
    // Runs after the approximated buffers are drawn and before PreShadow is filtered.
    void Run(FrameContext frame);
}
