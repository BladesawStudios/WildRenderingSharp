using System.Numerics;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The uniform blocks of the game's gloom programs, laid out as their decompiled code reads them.</summary>
/// <remarks>
/// The programs name the same slot differently from stage to stage and variant to variant - the shape pass's vertex stage reads the view at slot 3 and its
/// fragment stage at slot 4 - so a block is bound by the name each program gives it (see <see cref="GloomPrograms"/>) and only its role is fixed here.
/// The view block is the game's camera as the gloom reads it; the area block holds one volume, its unit-to-world rows first, which the vertex stage reads, then the
/// constants of its shape, which the fragment stage reads; the compose block is the renderer's parameters.
/// </remarks>
static class GloomBlocks
{
    public static readonly UboSpec View = new("GloomView", 12, 15 * 16);
    public static readonly UboSpec Area = new("GloomArea", 13, 8 * 16);
    public static readonly UboSpec Compose = new("GloomCompose", 14, 10 * 16);

    // Only the shape programs' fragment stage reads this one, at slot 146 for the index of the pixel's tile in a culling bitmap nothing here uses.
    public static readonly UboSpec Context = new("GloomContext", 15, 147 * 16);

    // The distance scale at the lens's field of view: one at fifty degrees and wider, less when narrower.
    static float DistanceScale(float verticalFovRadians)
    {
        float k = MathF.Max(0.87266463f / verticalFovRadians - 1f, 0f) * 0.3f + 1f;
        return 1f / k;
    }

    public static Ubo BuildView(in CameraData camera, int width, int height)
    {
        var block = new UboWriter(View);
        SetRows(block, 0, GpuMatrix.Rows(camera.View, 3));
        SetRows(block, 3, GpuMatrix.Rows(camera.Proj));
        SetRows(block, 7, GpuMatrix.Rows(camera.ViewInv, 3));
        block.Set(10, camera.TanHalf.X, camera.TanHalf.Y, 0f, 0f);
        block.Set(11, camera.Far - camera.Near, camera.Near, 1f - camera.Near / camera.Far, 0f);
        block.Set(12, width / 2f, height / 2f, 2f / width, 2f / height);
        float scale = DistanceScale(2f * MathF.Atan(camera.TanHalfFovY));
        block.Set(13, 1f / scale, scale, 0f, 0f);
        return block.ToUbo("gloom_view");
    }

    // A volume's block for a vertex stage that places its unit box and a fragment stage that measures against its shape.
    // Null for a volume whose box is flat, which has no inside.
    public static Ubo? BuildArea(in GloomVolume volume, in CameraData camera, bool cancelling)
    {
        var block = new UboWriter(Area);
        SetRows(block, 0, GpuMatrix.Rows(volume.UnitToWorld, 3));
        float fade = cancelling ? 0f : 1f;
        Matrix4x4 unitToView = volume.UnitToWorld * camera.View;
        if (volume.Shape == GloomShape.Sphere)
        {
            // The sphere ends where the program's falloff reaches 0.12, which is its unit box's half width, so the radius it measures against is that over 0.88.
            Vector3 centre = Vector3.Transform(Vector3.Zero, unitToView);
            float radius = 0.5f * new Vector3(volume.UnitToWorld.M11, volume.UnitToWorld.M12, volume.UnitToWorld.M13).Length();
            block.Set(3, centre, radius / 0.88f);
            block.Set(4, volume.Density.X, 0f, fade, 0f);
            return block.ToUbo("gloom_area");
        }

        if (!Matrix4x4.Invert(unitToView, out Matrix4x4 viewToUnit))
            return null;
        SetRows(block, 3, GpuMatrix.Rows(viewToUnit, 3));
        if (volume.Shape == GloomShape.Box)
            block.Set(6, volume.Density.X, 0f, fade, 0f);
        else
        {
            block.Set(6, volume.Density.X, volume.Density.Y, volume.NoiseScale, fade);
            block.Set(7, volume.InnerRatio.X, volume.InnerRatio.Y, 0f, 0f);
        }
        return block.ToUbo("gloom_area");
    }

    // The Depths' altitude gloom takes only its density.
    public static Ubo BuildAltitudeArea(float density)
    {
        var block = new UboWriter(Area);
        block.Set(0, density, 0f, 0f, 0f);
        return block.ToUbo("gloom_area");
    }

    public static Ubo BuildCompose(in GloomLook look)
    {
        var block = new UboWriter(Compose);
        block.Set(1, look.DarkDump);
        block.Set(2, look.BaseColor0);
        block.SetXyz(3, look.BaseColor1);
        block.SetXyz(4, look.BlackColor);
        block.Set(5, look.Intensity.X, look.Intensity.Y, 0f, 0f);
        block.Set(6, look.RedPattern.X, look.RedPattern.Y, 0f, 0f);
        block.SetXyz(7, look.GrainColor);
        block.Set(8, look.GrainPattern.X, look.GrainPattern.Y, 0f, 0f);
        block.SetXyz(9, look.Clock);
        return block.ToUbo("gloom_compose");
    }

    static void SetRows(UboWriter block, int first, ReadOnlySpan<Vector4> rows)
    {
        for (int i = 0; i < rows.Length; i++)
            block.Set(first + i, rows[i]);
    }
}
