using System;
using System.Collections.Generic;
using System.Numerics;
using WildRenderingSharp.Cloth.Model.HelperBone;
using WildRenderingSharp.Cloth.Simulation.Curves;

namespace WildRenderingSharp.Cloth.Simulation.HelperBone;

/// <summary>
/// Runtime solver for Nintendo Phive Helper Bones (.bphhb).
/// Decomposes driver bone relative transforms, evaluates cubic Hermite connection curves,
/// aggregates multi-driver outputs, and solves procedural pose-driven bone transforms.
/// </summary>
public sealed class HelperBoneSolver
{
    private readonly HelperBoneData _data;
    private readonly float[] _driverRoll;
    private readonly float[] _driverBendH;
    private readonly float[] _driverBendV;
    private readonly float[] _curveValues;
    private readonly float[] _outputValues;
    private readonly Matrix4x4[] _poseTransforms;

    public HelperBoneData Data => _data;

    public HelperBoneSolver(HelperBoneData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _driverRoll = new float[data.DriverBones.Count];
        _driverBendH = new float[data.DriverBones.Count];
        _driverBendV = new float[data.DriverBones.Count];
        _curveValues = new float[data.ConnectionCurves.Count];
        _outputValues = new float[data.Outputs.Count];
        _poseTransforms = new Matrix4x4[data.PoseDrivens.Count];
    }

    /// <summary>
    /// Solves helper bones in-place on an array of bone transforms indexed by <see cref="HelperBoneData.Bones"/>.
    /// </summary>
    public void Solve(Matrix4x4[] transforms)
    {
        if (transforms == null) throw new ArgumentNullException(nameof(transforms));

        // 1. Evaluate driver bones
        for (int d = 0; d < _data.DriverBones.Count; d++)
        {
            var driver = _data.DriverBones[d];
            if (driver.BoneId < 0 || driver.BoneId >= transforms.Length ||
                driver.BaseBoneId < 0 || driver.BaseBoneId >= transforms.Length)
            {
                _driverRoll[d] = 0f;
                _driverBendH[d] = 0f;
                _driverBendV[d] = 0f;
                continue;
            }

            Matrix4x4 mDriver = transforms[driver.BoneId];
            Matrix4x4 mBase = transforms[driver.BaseBoneId];

            if (!Matrix4x4.Invert(mBase, out var mBaseInv))
            {
                _driverRoll[d] = 0f;
                _driverBendH[d] = 0f;
                _driverBendV[d] = 0f;
                continue;
            }

            // Relative matrix in base bone's space
            Matrix4x4 mRel = mDriver * mBaseInv;
            Matrix4x4.Decompose(mRel, out _, out Quaternion rRel, out _);
            rRel = Quaternion.Normalize(rRel);

            // Delta rotation from authored base pose rotation
            Quaternion deltaR = Quaternion.Normalize(Quaternion.Inverse(driver.BaseRotate) * rRel);

            // Swing-twist decomposition about AimAxis
            Vector3 a = Vector3.Normalize(driver.AimAxis);
            Vector3 u = Vector3.Normalize(driver.UpAxis);
            Vector3 s = Vector3.Normalize(Vector3.Cross(a, u));

            // Twist part: projection of quaternion vector part onto aim axis
            Vector3 v = new Vector3(deltaR.X, deltaR.Y, deltaR.Z);
            Vector3 proj = Vector3.Dot(v, a) * a;
            Quaternion qTwist = new Quaternion(proj, deltaR.W);
            float twistLenSq = qTwist.LengthSquared();

            if (twistLenSq > 1e-8f)
            {
                qTwist = Quaternion.Normalize(qTwist);
            }
            else
            {
                qTwist = Quaternion.Identity;
            }

            // Twist angle (roll along aim axis)
            float roll = 2.0f * MathF.Atan2(Vector3.Dot(new Vector3(qTwist.X, qTwist.Y, qTwist.Z), a), qTwist.W);
            if (roll > MathF.PI) roll -= 2.0f * MathF.PI;
            if (roll < -MathF.PI) roll += 2.0f * MathF.PI;
            _driverRoll[d] = roll;

            // Swing part: deltaR = qSwing * qTwist => qSwing = deltaR * qTwist^-1
            Quaternion qSwing = Quaternion.Normalize(deltaR * Quaternion.Inverse(qTwist));

            // Swing transforms the aim axis to a new direction
            Vector3 aPrime = Vector3.Transform(a, qSwing);

            // BendH: angle in horizontal plane (a, s) around u
            float bendH = MathF.Atan2(Vector3.Dot(aPrime, s), Vector3.Dot(aPrime, a));
            _driverBendH[d] = bendH;

            // BendV: angle in vertical plane (a, u) around s
            float bendV = MathF.Atan2(-Vector3.Dot(aPrime, u), Vector3.Dot(aPrime, a));
            _driverBendV[d] = bendV;
        }

        // 2. Evaluate connection curves
        for (int c = 0; c < _data.ConnectionCurves.Count; c++)
        {
            var curve = _data.ConnectionCurves[c];
            float inputVal = 0f;

            if (curve.DriverBoneId >= 0 && curve.DriverBoneId < _data.DriverBones.Count)
            {
                inputVal = curve.Attr switch
                {
                    DriverAttribute.Roll => _driverRoll[curve.DriverBoneId],
                    DriverAttribute.BendH => _driverBendH[curve.DriverBoneId],
                    DriverAttribute.BendV => _driverBendV[curve.DriverBoneId],
                    _ => 0f
                };
            }

            _curveValues[c] = HermiteCurve.Evaluate(curve.Keys, inputVal);
        }

        // 3. Aggregate outputs (sum of connections)
        for (int o = 0; o < _data.Outputs.Count; o++)
        {
            var output = _data.Outputs[o];
            float sum = 0f;
            for (int i = 0; i < output.ConnectionCurveIds.Count; i++)
            {
                int curveId = output.ConnectionCurveIds[i];
                if (curveId >= 0 && curveId < _curveValues.Length)
                {
                    sum += _curveValues[curveId];
                }
            }
            _outputValues[o] = sum;
        }

        // 4. Evaluate pose drivens
        for (int p = 0; p < _data.PoseDrivens.Count; p++)
        {
            var pose = _data.PoseDrivens[p];

            float roll = ResolveChannel(pose.Roll);
            float bendH = ResolveChannel(pose.BendH);
            float bendV = ResolveChannel(pose.BendV);
            float tx = ResolveChannel(pose.TranslateX);
            float ty = ResolveChannel(pose.TranslateY);
            float tz = ResolveChannel(pose.TranslateZ);

            Vector3 a = Vector3.Normalize(pose.AimAxis);
            Vector3 u = Vector3.Normalize(pose.UpAxis);
            Vector3 s = Vector3.Normalize(Vector3.Cross(a, u));

            Quaternion qRoll = Quaternion.CreateFromAxisAngle(a, roll);
            Quaternion qBendH = Quaternion.CreateFromAxisAngle(u, bendH);
            Quaternion qBendV = Quaternion.CreateFromAxisAngle(s, bendV);

            // Combined procedural delta rotation
            Quaternion qDelta = qBendH * qBendV * qRoll;
            Quaternion rLocal = Quaternion.Normalize(pose.BaseRotate * qDelta);

            Vector3 tLocal = pose.BaseTranslate + new Vector3(tx, ty, tz);

            Matrix4x4 mLocal = Matrix4x4.CreateFromQuaternion(rLocal) * Matrix4x4.CreateTranslation(tLocal);

            Matrix4x4 mBase = Matrix4x4.Identity;
            if (pose.BaseBoneId >= 0 && pose.BaseBoneId < transforms.Length)
            {
                mBase = transforms[pose.BaseBoneId];
            }

            _poseTransforms[p] = mLocal * mBase;
        }

        // 5. Update driven bones
        for (int db = 0; db < _data.DrivenBones.Count; db++)
        {
            var driven = _data.DrivenBones[db];
            if (driven.BoneId < 0 || driven.BoneId >= transforms.Length)
                continue;

            bool hasRotate = driven.RotateDrivenType == 0 && driven.RotateDrivenId >= 0 && driven.RotateDrivenId < _poseTransforms.Length;
            bool hasTranslate = driven.TranslateDrivenType == 0 && driven.TranslateDrivenId >= 0 && driven.TranslateDrivenId < _poseTransforms.Length;

            if (hasRotate && hasTranslate && driven.RotateDrivenId == driven.TranslateDrivenId)
            {
                transforms[driven.BoneId] = _poseTransforms[driven.RotateDrivenId];
            }
            else if (hasRotate && !hasTranslate)
            {
                Matrix4x4.Decompose(_poseTransforms[driven.RotateDrivenId], out _, out Quaternion r, out _);
                Matrix4x4.Decompose(transforms[driven.BoneId], out Vector3 s, out _, out Vector3 t);
                transforms[driven.BoneId] = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
            }
            else if (hasTranslate && !hasRotate)
            {
                Vector3 t = _poseTransforms[driven.TranslateDrivenId].Translation;
                Matrix4x4.Decompose(transforms[driven.BoneId], out Vector3 s, out Quaternion r, out _);
                transforms[driven.BoneId] = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
            }
            else if (hasRotate && hasTranslate)
            {
                Matrix4x4.Decompose(_poseTransforms[driven.RotateDrivenId], out _, out Quaternion r, out _);
                Vector3 t = _poseTransforms[driven.TranslateDrivenId].Translation;
                Matrix4x4.Decompose(transforms[driven.BoneId], out Vector3 s, out _, out _);
                transforms[driven.BoneId] = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
            }
        }
    }

    private float ResolveChannel(ChannelBinding binding)
    {
        if (binding.IsBound)
        {
            return (binding.OutputId >= 0 && binding.OutputId < _outputValues.Length)
                ? _outputValues[binding.OutputId]
                : 0f;
        }
        return binding.Constant;
    }
}
