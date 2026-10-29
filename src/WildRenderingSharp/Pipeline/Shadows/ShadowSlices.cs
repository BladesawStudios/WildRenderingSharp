using System.Numerics;
using WildRenderingSharp.Graphics.Data;

namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>Fits a shadow cascade to a slice of the camera's view: the part of its frustum between two view depths.</summary>
/// <remarks>
/// The game cuts the view depth into slices and gives each its own map, so the nearest map holds only what is in front of the camera. The fit is a sphere around the
/// slice's eight corners, which does not change as the camera turns, so the map's texels stay the same size and only shift as the camera moves.
/// </remarks>
static class ShadowSlices
{
    public static ShadowFocus Fit(in CameraData camera, float start, float end)
    {
        var position = camera.ViewInv.Translation;
        var forward = -new Vector3(camera.ViewInv.M31, camera.ViewInv.M32, camera.ViewInv.M33);

        // How far a corner of the view is from its axis, for each metre of depth.
        float tanHalfY = camera.TanHalfFovY, tanHalfX = tanHalfY * camera.Aspect;
        float spread = MathF.Sqrt(tanHalfX * tanHalfX + tanHalfY * tanHalfY);

        // The sphere that touches the near and far corners alike is centred this far along the axis; where a wide view puts it past the far plane,
        // the far plane's own circle is the sphere.
        float centre = MathF.Min((1f + spread * spread) * (start + end) * 0.5f, end);
        float radius = MathF.Sqrt((end - centre) * (end - centre) + end * spread * end * spread);
        return new ShadowFocus(position + forward * centre, radius);
    }
}
