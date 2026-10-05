using System.Collections.Generic;
using System.Numerics;

namespace WildRenderingSharp.Cloth.Model.HelperBone;

/// <summary>
/// Root data structure for a Phive Helper Bone (.bphhb) definition.
/// Helper bones procedurally drive secondary skeleton bones (shoulder armor pads, skirt anchors,
/// waist belts, joint correctives) based on the pose of driver bones (arms, legs, pelvis).
/// </summary>
public class HelperBoneData
{
    public List<string> Bones { get; set; } = new();
    public List<DriverBone> DriverBones { get; set; } = new();
    public List<ConnectionCurve> ConnectionCurves { get; set; } = new();
    public List<Output> Outputs { get; set; } = new();
    public List<DrivenBone> DrivenBones { get; set; } = new();
    public List<PoseDriven> PoseDrivens { get; set; } = new();
}

/// <summary>
/// A driver bone observes the relative transform of a skeleton bone against a base bone.
/// Swing-twist decomposition extracts roll (twist along AimAxis), BendH (horizontal swing around UpAxis),
/// and BendV (vertical swing around SideAxis).
/// </summary>
public class DriverBone
{
    public int BoneId { get; set; }
    public int BaseBoneId { get; set; }
    public Vector3 BaseTranslate { get; set; } = Vector3.Zero;
    public Quaternion BaseRotate { get; set; } = Quaternion.Identity;
    public Vector3 AimAxis { get; set; } = Vector3.UnitX;
    public Vector3 UpAxis { get; set; } = Vector3.UnitY;
}

/// <summary>
/// Attribute of the driver bone motion observed by a curve:
/// 0 = Roll (twist around aim_axis)
/// 1 = BendH (horizontal swing bend around up_axis)
/// 2 = BendV (vertical swing bend around side_axis = aim x up)
/// </summary>
public enum DriverAttribute
{
    Roll = 0,
    BendH = 1,
    BendV = 2
}

/// <summary>
/// A connection curve evaluates a cubic Hermite spline over driver angle/translation inputs.
/// </summary>
public class ConnectionCurve
{
    public int DriverBoneId { get; set; }
    public DriverAttribute Attr { get; set; }
    public List<HermiteKey> Keys { get; set; } = new();
}

/// <summary>
/// A cubic Hermite keyframe: (Time, Value, InSlope, OutSlope).
/// In Nintendo AAMP: key = Vector4F(Time, Value, InSlope, OutSlope).
/// </summary>
public struct HermiteKey
{
    public float Time;
    public float Value;
    public float InSlope;
    public float OutSlope;

    public HermiteKey(float time, float value, float inSlope, float outSlope)
    {
        Time = time;
        Value = value;
        InSlope = inSlope;
        OutSlope = outSlope;
    }
}

/// <summary>
/// Aggregates evaluations from one or more connection curves by summing their values.
/// </summary>
public class Output
{
    public List<int> ConnectionCurveIds { get; set; } = new();
}

/// <summary>
/// Maps a skeleton bone to its procedural transform driver.
/// </summary>
public class DrivenBone
{
    public int BoneId { get; set; }
    public int TranslateDrivenType { get; set; }
    public int TranslateDrivenId { get; set; }
    public int RotateDrivenType { get; set; }
    public int RotateDrivenId { get; set; }
}

/// <summary>
/// Channel binding: either a constant scalar or bound to an Output index.
/// </summary>
public struct ChannelBinding
{
    public bool IsBound;
    public int OutputId;
    public float Constant;

    public static ChannelBinding FromConstant(float val) => new() { IsBound = false, Constant = val };
    public static ChannelBinding FromOutput(int id) => new() { IsBound = true, OutputId = id };
}

/// <summary>
/// Computes local transform relative to BaseBoneId using swing-twist Euler-like channels
/// (roll, bendH, bendV) and translations (translateX, translateY, translateZ).
/// </summary>
public class PoseDriven
{
    public int BaseBoneId { get; set; }
    public Vector3 BaseTranslate { get; set; } = Vector3.Zero;
    public Quaternion BaseRotate { get; set; } = Quaternion.Identity;
    public Vector3 AimAxis { get; set; } = Vector3.UnitX;
    public Vector3 UpAxis { get; set; } = Vector3.UnitY;

    public ChannelBinding Roll { get; set; }
    public ChannelBinding BendH { get; set; }
    public ChannelBinding BendV { get; set; }
    public ChannelBinding TranslateX { get; set; }
    public ChannelBinding TranslateY { get; set; }
    public ChannelBinding TranslateZ { get; set; }
}
