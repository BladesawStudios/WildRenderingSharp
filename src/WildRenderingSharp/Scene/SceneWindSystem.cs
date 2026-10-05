using System.Numerics;

namespace WildRenderingSharp.Scene;

/// <summary>
/// One continuous world wind field shared by every cloth instance in the scene. Direction changes
/// use seeded, smoothly-interpolated value noise: repeatable for a given seed and free of the
/// frame-to-frame random impulses that make constrained cloth chatter.
/// </summary>
public sealed class SceneWindSystem
{
    public bool Enabled { get; set; }
    public float Speed { get; set; } = 5f;
    public Vector3 PrevailingDirection { get; set; } = Vector3.UnitX;
    public bool VaryDirection { get; set; } = true;
    public float WanderDegrees { get; set; } = 35f;
    public float DirectionChangeSeconds { get; set; } = 3.5f;
    public float Turbulence { get; set; } = 0.2f;
    public int Seed { get; set; } = 1337;
    public bool CurveEnabled { get; set; }
    public float LoopDuration { get; set; } = 4f;
    public float CurvePhase { get; private set; }
    public WindStrengthCurve StrengthCurve { get; } = new();
    public float ElapsedSeconds { get; private set; }

    public float CurrentStrength => Enabled
        ? Speed * (CurveEnabled ? StrengthCurve.Evaluate(CurvePhase) : 1f) *
          MathF.Max(0f, 1f + Turbulence * Noise(ElapsedSeconds / MathF.Max(DirectionChangeSeconds * 0.45f, 0.05f), Seed + 71))
        : 0f;

    public Vector3 CurrentDirection
    {
        get
        {
            Vector3 basis = PrevailingDirection.LengthSquared() > 1e-8f
                ? Vector3.Normalize(PrevailingDirection)
                : Vector3.UnitX;
            if (!VaryDirection || WanderDegrees <= 0f) return basis;

            float t = ElapsedSeconds / MathF.Max(DirectionChangeSeconds, 0.05f);
            float yaw = Noise(t, Seed) * WanderDegrees * MathF.PI / 180f;
            float lift = Noise(t * 0.73f + 19.1f, Seed + 37) * MathF.Sin(WanderDegrees * MathF.PI / 180f) * 0.35f;
            float c = MathF.Cos(yaw);
            float s = MathF.Sin(yaw);
            Vector3 turned = new(basis.X * c - basis.Y * s, basis.X * s + basis.Y * c, basis.Z + lift);
            return turned.LengthSquared() > 1e-8f ? Vector3.Normalize(turned) : basis;
        }
    }

    public Vector3 CurrentForce => CurrentDirection * CurrentStrength;

    public bool Advance(float deltaSeconds)
    {
        if (!Enabled) return false;
        ElapsedSeconds += Math.Clamp(deltaSeconds, 0f, 0.1f);
        CurvePhase = (CurvePhase + deltaSeconds / MathF.Max(LoopDuration, 0.1f)) % 1f;
        return true;
    }

    public void ResetTime()
    {
        ElapsedSeconds = 0f;
        CurvePhase = 0f;
    }

    private static float Noise(float x, int seed)
    {
        int i = (int)MathF.Floor(x);
        float f = x - i;
        float smooth = f * f * (3f - 2f * f);
        return Lerp(Hash(i, seed), Hash(i + 1, seed), smooth) * 2f - 1f;
    }

    private static float Hash(int x, int seed)
    {
        uint h = unchecked((uint)x * 0x9E3779B9u + (uint)seed * 0x85EBCA6Bu);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h & 0x00FFFFFFu) / 16777215f;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
