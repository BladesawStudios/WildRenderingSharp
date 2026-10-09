using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Euler-angle rotation matrix builders, returning 3x4 matrices to match the
/// shaders' format (see <see cref="CameraData"/>).
/// </summary>
public static class EulerRotation
{
    /// <summary>Rotates about X, then Y, then Z.</summary>
    public static Vector4[] MakeXyzRows3(float rxDegrees, float ryDegrees, float rzDegrees) => CameraData.Rows(
        Matrix4x4.CreateRotationX(float.DegreesToRadians(rxDegrees))
        * Matrix4x4.CreateRotationY(float.DegreesToRadians(ryDegrees))
        * Matrix4x4.CreateRotationZ(float.DegreesToRadians(rzDegrees)), 3);

    /// <summary>Scales, rolls, pitches and yaws about <paramref name="pivot"/>, then moves by <paramref name="translation"/>.</summary>
    public static Vector4[] MakeYawPitchRollScaleAboutPivot(float yawRadians, float pitchRadians, float rollRadians, Vector3 scale, Vector3 pivot, Vector3 translation) =>
        CameraData.Rows(
            Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateScale(scale)
            * YawPitchRoll(yawRadians, pitchRadians, rollRadians)
            * Matrix4x4.CreateTranslation(pivot + translation), 3);

    public static (Vector3 Right, Vector3 Up, Vector3 Forward) YawPitchRollBasis(float yawRadians, float pitchRadians, float rollRadians)
    {
        var rotation = YawPitchRoll(yawRadians, pitchRadians, rollRadians);
        return (
            Vector3.TransformNormal(Vector3.UnitX, rotation),
            Vector3.TransformNormal(Vector3.UnitY, rotation),
            Vector3.TransformNormal(Vector3.UnitZ, rotation));
    }

    // Roll about Z, then pitch about X, then yaw about Z (Z is up).
    static Matrix4x4 YawPitchRoll(float yawRadians, float pitchRadians, float rollRadians) =>
        Matrix4x4.CreateRotationZ(rollRadians) * Matrix4x4.CreateRotationX(pitchRadians) * Matrix4x4.CreateRotationZ(yawRadians);
}
