using System.Numerics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Effects;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Lays the game's field haze over the tonemapped image, where it hides the distance.</summary>
internal sealed class FieldHazeStage(StageServices services) : IFrameStage, IDisposable
{
    readonly FieldHazePass? _haze = FieldHazePass.Create(services.Gl, services.Directories);

    public void Run(FrameContext frame)
    {
        var environment = frame.TotkEnvironment();
        if (_haze is not null && environment.Settings.UseFieldHaze)
            _haze.Run(services.Resources, frame.Targets, frame.Targets.Ldr, frame.Camera, Tint(environment.Palette.FogColor), environment.Settings.FieldHazeStrength, environment.Settings.FieldHazeFalloff, environment.Settings.FieldHazeSkyInfluence, environment.Settings.FieldHazeGloomScale);
    }

    // The haze takes the green of the palette's fog colour: the colour the game's own haze sprites show against the scene is a pale warm one, and a screenshot of the
    // game shows it lifted toward the fog's greenish white.
    // TODO: how much of the fog colour reaches the haze is fitted to one screenshot; the sprites' own shading is not run.
    static Vector3 Tint(Vector3 fogColor)
    {
        float peak = MathF.Max(fogColor.X, MathF.Max(fogColor.Y, fogColor.Z));
        Vector3 hue = peak > 1e-6f ? fogColor / peak : Vector3.One;
        return Vector3.Lerp(Vector3.One, hue, 0.6f);
    }

    public void Dispose() => _haze?.Dispose();
}
