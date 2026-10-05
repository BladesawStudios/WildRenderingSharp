using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>Euler-angle rotation matrix builders. Mirrors <c>render_deferred_master_sword.make_euler_matrix</c> (XYZ order only - the only order TotK's icon-capture preset and the default model tilt actually use).</summary>
public static class EulerRotation
{
    /// <summary>4-row rotation matrix (no translation) applying X, then Y, then Z - i.e. <c>v' = Rz * (Ry * (Rx * v))</c>, matching <c>make_euler_matrix(..., order="xyz")</c>.</summary>
    public static Vector4[] MakeXyzRows4(float rxDegrees, float ryDegrees, float rzDegrees)
    {
        float rx = float.DegreesToRadians(rxDegrees), ry = float.DegreesToRadians(ryDegrees), rz = float.DegreesToRadians(rzDegrees);
        float cx = MathF.Cos(rx), sx = MathF.Sin(rx);
        float cy = MathF.Cos(ry), sy = MathF.Sin(ry);
        float cz = MathF.Cos(rz), sz = MathF.Sin(rz);

        Vector4[] mx = [new(1, 0, 0, 0), new(0, cx, -sx, 0), new(0, sx, cx, 0), new(0, 0, 0, 1)];
        Vector4[] my = [new(cy, 0, sy, 0), new(0, 1, 0, 0), new(-sy, 0, cy, 0), new(0, 0, 0, 1)];
        Vector4[] mz = [new(cz, -sz, 0, 0), new(sz, cz, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1)];

        return Mat4Math.Multiply(mz, Mat4Math.Multiply(my, mx));
    }

    public static Vector4[] MakeXyzRows3(float rxDegrees, float ryDegrees, float rzDegrees) => MakeXyzRows4(rxDegrees, ryDegrees, rzDegrees)[..3];

    /// <summary>
    /// Yaw (about Z) then pitch (about X) - <c>Rz(yaw) * Rx(pitch)</c> - applied about a pivot
    /// point rather than the origin, matching <c>Viewer._model_matrix</c>'s free-camera branch
    /// (arrow keys adjust yaw/pitch; the model spins in place about its own bounds centre rather
    /// than around world origin).
    /// </summary>
    public static Vector4[] MakeYawPitchAboutPivot(float yawRadians, float pitchRadians, Vector3 pivot)
    {
        float cy = MathF.Cos(yawRadians), sy = MathF.Sin(yawRadians);
        float cp = MathF.Cos(pitchRadians), sp = MathF.Sin(pitchRadians);

        // R = Rz(yaw) * Rx(pitch), expanded by hand (both are simple enough that composing via
        // Mat4Math would just be extra ceremony for a 3x3 result).
        Vector3 row0 = new(cy, -sy * cp, sy * sp);
        Vector3 row1 = new(sy, cy * cp, -cy * sp);
        Vector3 row2 = new(0f, sp, cp);

        // Affine-about-pivot translation, per row: pivot - R*pivot.
        Vector3 rPivot = new(Vector3.Dot(row0, pivot), Vector3.Dot(row1, pivot), Vector3.Dot(row2, pivot));
        return
        [
            new(row0.X, row0.Y, row0.Z, pivot.X - rPivot.X),
            new(row1.X, row1.Y, row1.Z, pivot.Y - rPivot.Y),
            new(row2.X, row2.Y, row2.Z, pivot.Z - rPivot.Z),
        ];
    }

    /// <summary>
    /// Generalises <see cref="MakeYawPitchAboutPivot"/> for <c>PlacedActor</c>'s full G/R/S
    /// transform-gizmo support: adds Roll (a further LOCAL rotation about the object's own Z axis,
    /// applied before yaw/pitch map it into world space - once composed this is equivalent to
    /// spinning about the object's own current forward direction in world space, the same thing
    /// <c>SceneCamera.Up()</c> achieves via a separate Rodrigues step for its own Roll) and
    /// non-uniform Scale (applied in LOCAL space, i.e. R * S, so scaling doesn't also skew world
    /// axes). Both rotation AND scale pivot about <paramref name="pivot"/> (so scaling/rotating in
    /// place doesn't also shift the object - same "rotate about a fixed point" trick
    /// <see cref="MakeYawPitchAboutPivot"/> already uses, just generalised to R*S instead of R
    /// alone), with <paramref name="translation"/> added on top as an INDEPENDENT world-space
    /// offset - not folded into the pivot itself, which would make the offset's effect a function
    /// of the current rotation instead of a plain additive move. Reduces to
    /// <see cref="MakeYawPitchAboutPivot"/>'s own result when roll=0, scale=(1,1,1) and
    /// translation=0.
    /// </summary>
    public static Vector4[] MakeYawPitchRollScaleAboutPivot(float yawRadians, float pitchRadians, float rollRadians, Vector3 scale, Vector3 pivot, Vector3 translation)
    {
        float cy = MathF.Cos(yawRadians), sy = MathF.Sin(yawRadians);
        float cp = MathF.Cos(pitchRadians), sp = MathF.Sin(pitchRadians);
        float cr = MathF.Cos(rollRadians), sr = MathF.Sin(rollRadians);

        Vector4[] rzYaw = [new(cy, -sy, 0, 0), new(sy, cy, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1)];
        Vector4[] rxPitch = [new(1, 0, 0, 0), new(0, cp, -sp, 0), new(0, sp, cp, 0), new(0, 0, 0, 1)];
        Vector4[] rzRoll = [new(cr, -sr, 0, 0), new(sr, cr, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1)];
        Vector4[] s = [new(scale.X, 0, 0, 0), new(0, scale.Y, 0, 0), new(0, 0, scale.Z, 0), new(0, 0, 0, 1)];

        var r = Mat4Math.Multiply(rzYaw, Mat4Math.Multiply(rxPitch, rzRoll));
        var m = Mat4Math.Multiply(r, s);

        Vector3 row0 = new(m[0].X, m[0].Y, m[0].Z);
        Vector3 row1 = new(m[1].X, m[1].Y, m[1].Z);
        Vector3 row2 = new(m[2].X, m[2].Y, m[2].Z);
        Vector3 mPivot = new(Vector3.Dot(row0, pivot), Vector3.Dot(row1, pivot), Vector3.Dot(row2, pivot));

        return
        [
            new(row0.X, row0.Y, row0.Z, pivot.X - mPivot.X + translation.X),
            new(row1.X, row1.Y, row1.Z, pivot.Y - mPivot.Y + translation.Y),
            new(row2.X, row2.Y, row2.Z, pivot.Z - mPivot.Z + translation.Z),
        ];
    }

    /// <summary>The world-space direction of the object's own local Right/Up/Forward axes after a yaw-pitch-roll rotation - the rotation-only (pivot at origin, no scale/translation) case of <see cref="MakeYawPitchRollScaleAboutPivot"/>, used by anything (the transform gizmo's rotate rings, <c>PlacedActor.Basis</c>) that needs the object's own axes rather than a full vertex transform.</summary>
    public static (Vector3 Right, Vector3 Up, Vector3 Forward) YawPitchRollBasis(float yawRadians, float pitchRadians, float rollRadians)
    {
        var rows = MakeYawPitchRollScaleAboutPivot(yawRadians, pitchRadians, rollRadians, Vector3.One, Vector3.Zero, Vector3.Zero);
        return (
            new Vector3(rows[0].X, rows[1].X, rows[2].X),
            new Vector3(rows[0].Y, rows[1].Y, rows[2].Y),
            new Vector3(rows[0].Z, rows[1].Z, rows[2].Z));
    }
}
