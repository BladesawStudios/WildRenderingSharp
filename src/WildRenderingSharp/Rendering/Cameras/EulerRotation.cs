using System.Numerics;
using WildRenderingSharp.Graphics.Data;

namespace WildRenderingSharp.Rendering.Cameras;

/// <summary>
/// Euler-angle rotation matrix builders, returning 3x4 matrices to match the
/// shaders' format (see <see cref="CameraData"/>).
/// </summary>
public static class EulerRotation
{
    /// <summary>Scales, rolls, pitches and yaws about <paramref name="pivot"/>, then moves by <paramref name="translation"/>.</summary>
    public static Vector4[] MakeYawPitchRollScaleAboutPivot(float yawRadians, float pitchRadians, float rollRadians, Vector3 scale, Vector3 pivot, Vector3 translation) =>
        GpuMatrix.Rows(
            Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateScale(scale)
            * YawPitchRoll(yawRadians, pitchRadians, rollRadians)
            * Matrix4x4.CreateTranslation(pivot + translation), 3);

    // Roll about Z, then pitch about X, then yaw about Y (Y is up).
    static Matrix4x4 YawPitchRoll(float yawRadians, float pitchRadians, float rollRadians) =>
        Matrix4x4.CreateRotationZ(rollRadians) * Matrix4x4.CreateRotationX(pitchRadians) * Matrix4x4.CreateRotationY(yawRadians);
}
