using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Orbit, pan and first-person-fly camera rig feeding a <see cref="Camera"/>'s Eye and Target. Input handling and the model's own
/// rotation stay in the host; this class only knows the rig.
/// </summary>
public class OrbitCameraController
{
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Distance { get; set; } = 1f;
    public Vector3 Target { get; set; }
    public Vector3 Up { get; set; } = new(0, 0, 1);

    /// <summary>
    /// Dolly clamp range - set from <see cref="SceneFramingCalculator"/> per model, since a fixed sword-scale range makes anything
    /// bigger "stick" at max zoom-out.
    /// </summary>
    public float MinDistance { get; set; } = 0.4f;
    public float MaxDistance { get; set; } = 12f;

    /// <summary>Fly speed multiplier while RMB-held flying, trimmed by the scroll wheel.</summary>
    public float FlySpeed { get; set; } = 1f;

    public Vector3 Eye => Target + Offset(Yaw, Pitch) * Distance;

    static Vector3 Offset(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(cp * MathF.Cos(yaw), cp * MathF.Sin(yaw), MathF.Sin(pitch));
    }

    /// <summary>
    /// Sets Yaw/Pitch/Distance/Target so the rig currently looks from <paramref name="eye"/> at <paramref name="target"/> - used
    /// once, after framing a newly-loaded model.
    /// </summary>
    public void FrameTo(Vector3 eye, Vector3 target)
    {
        Target = target;
        var d = eye - target;
        Distance = MathF.Max(d.Length(), 1e-6f);
        Yaw = MathF.Atan2(d.Y, d.X);
        Pitch = MathF.Asin(Math.Clamp(d.Z / Distance, -1f, 1f));
    }

    /// <summary>LMB-drag: orbit the target, camera stays pointed at it.</summary>
    public void Orbit(float dxPixels, float dyPixels, float rate = 0.006f)
    {
        Yaw -= dxPixels * rate;
        Pitch = Math.Clamp(Pitch + dyPixels * rate, -1.5f, 1.5f);
    }

    /// <summary>MMB-drag: pan the target along the camera's own right/up axes, scaled by distance.</summary>
    public void Pan(float dxPixels, float dyPixels, float rate = 0.0018f)
    {
        var (_, right, up) = Basis();
        Target += (-right * dxPixels + up * dyPixels) * (rate * Distance);
    }

    public void Dolly(float scrollDelta)
    {
        Distance = Math.Clamp(Distance * MathF.Pow(0.9f, scrollDelta), MinDistance, MaxDistance);
    }

    /// <summary>Wheel-while-RMB-held: trims fly speed rather than dollying.</summary>
    public void TrimFlySpeed(float scrollDelta)
    {
        FlySpeed = Math.Clamp(FlySpeed * MathF.Pow(1.15f, scrollDelta), 0.05f, 20f);
    }

    /// <summary>RMB-held mouse-look: rotates about the EYE, not the orbit pivot - the pivot (Target) moves to keep the eye fixed.</summary>
    public void Look(float dxPixels, float dyPixels, float rate = 0.0032f)
    {
        var eye = Eye;
        Yaw -= dxPixels * rate;
        Pitch = Math.Clamp(Pitch + dyPixels * rate, -1.5f, 1.5f);
        Target = eye - Offset(Yaw, Pitch) * Distance;
    }

    /// <summary>Moves the eye (and target, rigidly) by a world-space delta - RMB-held WASDQE flight.</summary>
    public void Fly(Vector3 worldDelta) => Target += worldDelta;

    /// <summary>Forward/right/up in world space - what panning and flying move along.</summary>
    public (Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Up));
        return (forward, right, Vector3.Cross(right, forward));
    }
}
