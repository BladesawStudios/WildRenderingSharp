using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>Orbit, pan and first-person-fly camera rig feeding a <see cref="Camera"/>'s Eye and Target.</summary>
public class OrbitCameraController
{
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Distance { get; set; } = 1f;
    public Vector3 Target { get; set; }
    public Vector3 Up { get; set; } = new(0, 0, 1);

    public float MinDistance { get; set; } = 0.4f;
    public float MaxDistance { get; set; } = 12f;

    public float FlySpeed { get; set; } = 1f;

    public Vector3 Eye => Target + Offset(Yaw, Pitch) * Distance;

    static Vector3 Offset(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(cp * MathF.Cos(yaw), cp * MathF.Sin(yaw), MathF.Sin(pitch));
    }

    public void FrameTo(Vector3 eye, Vector3 target)
    {
        Target = target;
        var d = eye - target;
        Distance = MathF.Max(d.Length(), 1e-6f);
        Yaw = MathF.Atan2(d.Y, d.X);
        Pitch = MathF.Asin(Math.Clamp(d.Z / Distance, -1f, 1f));
    }

    public void Orbit(float dxPixels, float dyPixels, float rate = 0.006f)
    {
        Yaw -= dxPixels * rate;
        Pitch = Math.Clamp(Pitch + dyPixels * rate, -1.5f, 1.5f);
    }

    public void Pan(float dxPixels, float dyPixels, float rate = 0.0018f)
    {
        var (_, right, up) = Basis();
        Target += (-right * dxPixels + up * dyPixels) * (rate * Distance);
    }

    public void Dolly(float scrollDelta)
    {
        Distance = Math.Clamp(Distance * MathF.Pow(0.9f, scrollDelta), MinDistance, MaxDistance);
    }

    public void TrimFlySpeed(float scrollDelta)
    {
        FlySpeed = Math.Clamp(FlySpeed * MathF.Pow(1.15f, scrollDelta), 0.05f, 20f);
    }

    public void Look(float dxPixels, float dyPixels, float rate = 0.0032f)
    {
        var eye = Eye;
        Yaw -= dxPixels * rate;
        Pitch = Math.Clamp(Pitch + dyPixels * rate, -1.5f, 1.5f);
        Target = eye - Offset(Yaw, Pitch) * Distance;
    }

    public void Fly(Vector3 worldDelta) => Target += worldDelta;

    public (Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Up));
        return (forward, right, Vector3.Cross(right, forward));
    }
}
