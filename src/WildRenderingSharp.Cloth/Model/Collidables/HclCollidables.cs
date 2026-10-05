using System.Numerics;

namespace WildRenderingSharp.Cloth.Model.Collidables;

public enum HclShapeType : int
{
    Capsule = 0,
    Sphere = 1,
    TaperedCapsule = 2,
    Plane = 3,
    ConvexPlanes = 4,
    ConvexGeometry = 5,
    ConvexHeightField = 6
}

public abstract class HclShape
{
    public HclShapeType ShapeType { get; protected set; }
}

public sealed class HclCapsuleShape : HclShape
{
    public Vector3 Start { get; set; }
    public Vector3 End { get; set; }
    public Vector3 Direction { get; set; }
    public float Radius { get; set; }
    public float CapLenSqrdInv { get; set; }

    public HclCapsuleShape()
    {
        ShapeType = HclShapeType.Capsule;
    }
}

public sealed class HclSphereShape : HclShape
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }

    public HclSphereShape()
    {
        ShapeType = HclShapeType.Sphere;
    }
}

public sealed class HclTaperedCapsuleShape : HclShape
{
    public Vector3 Small { get; set; }
    public Vector3 Big { get; set; }
    public float SmallRadius { get; set; }
    public float BigRadius { get; set; }

    public HclTaperedCapsuleShape()
    {
        ShapeType = HclShapeType.TaperedCapsule;
    }
}

public sealed class HclPlaneShape : HclShape
{
    public Vector4 PlaneEquation { get; set; }

    public HclPlaneShape()
    {
        ShapeType = HclShapeType.Plane;
    }
}

public sealed class HclCollidable
{
    public string Name { get; set; } = string.Empty;
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
    public HclShape? Shape { get; set; }
    public float PinchDetectionRadius { get; set; }
    public sbyte PinchDetectionPriority { get; set; }
    public bool PinchDetectionEnabled { get; set; }
    public bool VirtualCollisionPointCollisionEnabled { get; set; }
    public bool Enabled { get; set; } = true;
    public uint TransformSetIndex { get; set; }
    public uint TransformIndex { get; set; }
}
