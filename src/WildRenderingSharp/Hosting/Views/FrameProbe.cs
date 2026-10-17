using System.Text;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Hosting.Views;

/// <summary>Describes the exact values of every input the deferred resolve reads, and what it wrote, at the middle of the frame (the same texel in each).</summary>
static class FrameProbe
{
    public static string Describe(GL gl, DeferredPipeline pipeline, RenderTargets targets)
    {
        var text = new StringBuilder();
        DescribeTextures(text, targets);
        DescribeUniformBlocks(text, pipeline.Resources);
        DescribeGlState(text, gl);
        return text.ToString();
    }

    static void DescribeTextures(StringBuilder text, RenderTargets t)
    {
        int x = t.Width / 2, y = t.Height / 2;
        string Bytes(GpuTexture g) { var v = t.ReadPixel(g, g.Width / 2, g.Height / 2) * 255f; return $"{v.X:F0} {v.Y:F0} {v.Z:F0} {v.W:F0}"; }
        string Floats(GpuTexture g) { var v = t.ReadPixel(g, g.Width / 2, g.Height / 2); return $"{v.X:G4} {v.Y:G4} {v.Z:G4} {v.W:G4}"; }

        text.AppendLine($"albedo (bytes)  {Bytes(t.GBuffer[1])}");
        text.AppendLine($"normal (bytes)  {Bytes(t.GBuffer[3])}");
        text.AppendLine($"emission  {Floats(t.GBuffer[5])}");
        text.AppendLine($"linear depth  {Floats(t.LinearDepth)}");
        text.AppendLine($"pre-shadow  {Floats(t.PreShadow)}");
        text.AppendLine($"pre-misc  {Floats(t.PreMisc)}");
        text.AppendLine($"pre-fog  {Floats(t.PreFog)}");
        text.AppendLine($"light field 0  {Floats(t.LayerCopy(t.FieldLightPrePassArray, 0))}");
        text.AppendLine($"light field 1  {Floats(t.LayerCopy(t.FieldLightPrePassArray, 1))}");
        text.AppendLine($"pass id  {Bytes(t.PassId)}");
        if (t.Stage(3) is { } chosen)
            text.AppendLine($"chosen pass output  {Floats(chosen)}");
        text.AppendLine($"HDR frame  {Floats(t.Final)}");

        // The pixels either side of the middle, horizontally and then vertically, for the buffers an edge shows in: where a rim starts and what each stage held there.
        text.AppendLine("--- across the middle, 3 pixels either side (x - 3 .. x + 3), then down it (y - 3 .. y + 3) ---");
        foreach (var (name, texture, scale) in new (string, GpuTexture, float)[]
        {
            ("emission", t.GBuffer[5], 1f), ("albedo", t.GBuffer[1], 255f), ("material", t.GBuffer[0], 255f), ("pass id", t.PassId, 255f),
            ("linear depth", t.LinearDepth, 1f), ("behind", t.Behind, 1f), ("HDR", t.Final, 1f),
        })
        {
            string Line(int dx, int dy) => string.Join(" | ", Enumerable.Range(-3, 7).Select(i =>
            {
                var v = t.ReadPixel(texture, Math.Clamp(x + dx * i, 0, texture.Width - 1), Math.Clamp(y + dy * i, 0, texture.Height - 1)) * scale;
                return scale == 1f ? $"{v.X:G3} {v.Y:G3} {v.Z:G3}" : $"{v.X:F0} {v.Y:F0} {v.Z:F0} {v.W:F0}";
            }));
            text.AppendLine($"{name,-13} x: {Line(1, 0)}");
            text.AppendLine($"{name,-13} y: {Line(0, 1)}");
        }
        text.AppendLine($"half depth centre  {Floats(t.LinearDepthHalf)}");
        // The scan reads the whole emission buffer back, so it runs a few times a second and the last result stands in between.
        if (_scanFrames++ % ScanEvery == 0)
        {
            _edge = DescribeBrightestLine(t);
            _dark = DescribeDarkestLine(t);
        }
        text.Append(_edge);
        text.Append(_dark);
    }

    // Finds, with no aiming, the thinnest bright line in the lit frame - a pixel well above both its neighbours along one axis, the way a rim is - and lists
    // what each buffer held for nine pixels across it. Its place is kept for drawing a marker on the view.
    static string DescribeBrightestLine(RenderTargets t)
    {
        var text = new StringBuilder();
        int width = t.Final.Width, height = t.Final.Height;
        float[] hdr = t.ReadPixelsFloatRgba(t.Final);   // rows from the top of the displayed frame
        float Luma(int px, int py) { int i = (py * width + px) * 4; return hdr[i] + hdr[i + 1] + hdr[i + 2]; }

        (int X, int Y, int DX, int DY, float Excess) best = (-1, -1, 0, 0, 0f);
        for (int py = 1; py < height - 1; py++)
        {
            for (int px = 1; px < width - 1; px++)
            {
                float luma = Luma(px, py);
                if (luma < 0.15f)
                    continue;
                float across = MathF.Max(Luma(px - 1, py), Luma(px + 1, py)), down = MathF.Max(Luma(px, py - 1), Luma(px, py + 1));
                if (luma - across > best.Excess && luma > 1.6f * across)
                    best = (px, py, 1, 0, luma - across);
                if (luma - down > best.Excess && luma > 1.6f * down)
                    best = (px, py, 0, 1, luma - down);
            }
        }

        _marker = best.X < 0 ? null : (best.X, best.Y, width, height);
        text.AppendLine("--- the brightest thin line in the lit frame ---");
        if (best.X < 0)
        {
            text.AppendLine("none");
            return text.ToString();
        }

        text.AppendLine($"at x {best.X}, y {best.Y} from the top ({100f * best.X / width:F0}% across, {100f * best.Y / height:F0}% down), a line along "
            + (best.DX == 1 ? "y" : "x") + $", {best.Excess:G3} brighter than its neighbours; nine pixels across it, {(best.DX == 1 ? "left to right" : "top to bottom")}, with it fifth");
        foreach (var (name, texture, scale) in new (string, GpuTexture, float)[]
        {
            ("emission", t.GBuffer[5], 1f), ("albedo", t.GBuffer[1], 255f), ("material", t.GBuffer[0], 255f), ("pass id", t.PassId, 255f),
            ("linear depth", t.LinearDepth, 1f), ("behind", t.Behind, 1f), ("HDR", t.Final, 1f),
        })
        {
            // The pass ID and the HDR are laid out from the bottom of the frame, the G-buffer and what is stored like it from the top.
            bool fromBottom = texture == t.PassId || texture == t.Final;
            string Cell(int i)
            {
                int cx = Math.Clamp(best.X + best.DX * (i - 4), 0, width - 1);
                int cy = Math.Clamp(best.Y + best.DY * (i - 4), 0, height - 1);
                var v = t.ReadPixel(texture, cx, fromBottom ? height - 1 - cy : cy) * scale;
                return scale == 1f ? $"{v.X:G3} {v.Y:G3} {v.Z:G3}" : $"{v.X:F0} {v.Y:F0} {v.Z:F0} {v.W:F0}";
            }
            text.AppendLine($"{name,-13} {string.Join(" | ", Enumerable.Range(0, 9).Select(Cell))}");
        }
        return text.ToString();
    }

    // The same scan for the opposite fault: a pixel far darker than both its neighbours along one axis, between two lit ones, the way the stray dark lines along a
    // silhouette or a corner are. Every buffer the resolve reads is listed across it, so one copy of the text says which of them holds the odd value.
    static string DescribeDarkestLine(RenderTargets t)
    {
        var text = new StringBuilder();
        int width = t.Final.Width, height = t.Final.Height;
        float[] hdr = t.ReadPixelsFloatRgba(t.Final);
        float Luma(int px, int py) { int i = (py * width + px) * 4; return hdr[i] + hdr[i + 1] + hdr[i + 2]; }

        (int X, int Y, int DX, int DY, float Drop) worst = (-1, -1, 0, 0, 0f);
        for (int py = 1; py < height - 1; py++)
        {
            for (int px = 1; px < width - 1; px++)
            {
                float luma = Luma(px, py);
                float acrossLow = MathF.Min(Luma(px - 1, py), Luma(px + 1, py)), downLow = MathF.Min(Luma(px, py - 1), Luma(px, py + 1));
                if (acrossLow > 0.45f && luma < 0.4f * acrossLow && acrossLow - luma > worst.Drop)
                    worst = (px, py, 1, 0, acrossLow - luma);
                if (downLow > 0.45f && luma < 0.4f * downLow && downLow - luma > worst.Drop)
                    worst = (px, py, 0, 1, downLow - luma);
            }
        }

        text.AppendLine("--- the darkest thin line in the lit frame ---");
        if (worst.X < 0)
        {
            text.AppendLine("none");
            return text.ToString();
        }

        text.AppendLine($"at x {worst.X}, y {worst.Y} from the top ({100f * worst.X / width:F0}% across, {100f * worst.Y / height:F0}% down), a line along "
            + (worst.DX == 1 ? "y" : "x") + $", {worst.Drop:G3} darker than its neighbours; nine pixels across it, {(worst.DX == 1 ? "left to right" : "top to bottom")}, with it fifth");
        foreach (var (name, texture, scale) in new (string, GpuTexture, float)[]
        {
            ("emission", t.GBuffer[5], 1f), ("albedo", t.GBuffer[1], 255f), ("normal", t.GBuffer[3], 255f), ("material", t.GBuffer[0], 255f), ("pass id", t.PassId, 255f),
            ("linear depth", t.LinearDepth, 1f), ("pre-shadow", t.PreShadow, 1f), ("pre-misc", t.PreMisc, 1f), ("pre-fog", t.PreFog, 1f), ("behind", t.Behind, 1f), ("HDR", t.Final, 1f),
        })
        {
            // The buffers drawn under the game's origin are laid out from the top of the frame; the pass ID and the HDR from the bottom.
            bool fromBottom = texture == t.PassId || texture == t.Final;
            bool half = texture == t.PreShadow || texture == t.PreMisc;
            string Cell(int i)
            {
                int cx = Math.Clamp(worst.X + worst.DX * (i - 4), 0, width - 1);
                int cy = Math.Clamp(worst.Y + worst.DY * (i - 4), 0, height - 1);
                if (half)
                    (cx, cy) = (cx / 2, cy / 2);
                var v = t.ReadPixel(texture, cx, fromBottom ? texture.Height - 1 - cy : cy) * scale;
                return scale == 1f ? $"{v.X:G3} {v.Y:G3} {v.Z:G3} {v.W:G3}" : $"{v.X:F0} {v.Y:F0} {v.Z:F0} {v.W:F0}";
            }
            text.AppendLine($"{name,-13} {string.Join(" | ", Enumerable.Range(0, 9).Select(Cell))}");
        }
        return text.ToString();
    }

    // Where the last scan found its line, in the pixels of the frame it scanned, for the view to mark.
    public static (int X, int Y, int Width, int Height)? Marker => _marker;
    static (int X, int Y, int Width, int Height)? _marker;

    const int ScanEvery = 30;
    static int _scanFrames;
    static string _edge = "", _dark = "";

    static void DescribeUniformBlocks(StringBuilder text, GLResourceCache resources)
    {
        byte[] env = resources.ReadUbo(FrameUniformKeys.Environment);
        byte[] sceneMat = resources.ReadUbo(FrameUniformKeys.SceneMaterial);
        byte[] context = resources.ReadUbo(FrameUniformKeys.SceneCamera);
        text.AppendLine("Env[5] " + Slot(env, 5) + " | [47] " + Slot(env, 47) + " | [70] " + Slot(env, 70));
        text.AppendLine("SceneMat[1] " + Slot(sceneMat, 1) + " | [2] " + Slot(sceneMat, 2) + " | [49] " + Slot(sceneMat, 49));
        text.AppendLine("Ctx[12] " + Slot(context, 12) + " | [14] " + Slot(context, 14));
        text.AppendLine("Ctx[16] " + Slot(context, 16) + " | [17] " + Slot(context, 17) + " | [18] " + Slot(context, 18));
    }

    static void DescribeGlState(StringBuilder text, GL gl)
    {
        Span<bool> mask = stackalloc bool[4];
        gl.GetBoolean(GetPName.ColorWritemask, mask);
        text.AppendLine($"GL colour mask {mask[0]} {mask[1]} {mask[2]} {mask[3]}; blend {gl.IsEnabled(EnableCap.Blend)}, " +
            $"scissor {gl.IsEnabled(EnableCap.ScissorTest)}, cull {gl.IsEnabled(EnableCap.CullFace)}, depth {gl.IsEnabled(EnableCap.DepthTest)}, stencil {gl.IsEnabled(EnableCap.StencilTest)}");
        Span<bool> indexed = stackalloc bool[4];
        for (uint i = 0; i < 2; i++)
        {
            gl.GetBoolean(GLEnum.ColorWritemask, i, indexed);
            text.AppendLine($"  indexed mask {i}: {indexed[0]} {indexed[1]} {indexed[2]} {indexed[3]}");
        }
    }

    static string Slot(byte[] block, int slot) => block.Length < (slot + 1) * 16
        ? "(out of range)"
        : string.Join(' ', Enumerable.Range(0, 4).Select(i => BitConverter.ToSingle(block, slot * 16 + i * 4).ToString("G5")));
}
