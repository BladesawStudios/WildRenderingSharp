// Created Oct 8 2026, adapted from Terrain Workbench
// (which in turn copied it from a C++ class in RenderTron 9000)
// @author Torphedo

using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Rendering;

public class Camera {
    public Vector3 Up = new(0, 1, 0);

    public Vector3 Eye = new(0, 200, 0);
    public Vector3 Target = new(0, 200, 0);

    // Projection settings
    public float FovRadians = Single.DegreesToRadians(70.0f);

    public float FovDegrees {
        get => Single.RadiansToDegrees(FovRadians);
        set =>  FovRadians = Single.DegreesToRadians(value);
    }
    public float Aspect = 16f / 9f;
    public float NearPlane = 0.1f;
    public float FarPlane = 10000.0f;

    // Get just the camera transform
    public Matrix4x4 view_matrix() {
        return Matrix4x4.CreateLookAt(Eye, Target, Up);
    }

    // Get just the projection transform
    public Matrix4x4 proj_matrix() {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, Aspect, NearPlane, FarPlane) * CameraData.ZeroToOneDepthToGl;
        return projection;
    }
};
