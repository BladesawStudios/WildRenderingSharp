using System.Text;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Gpu;

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
        string Bytes(GpuTexture g) { var v = t.ReadPixel(g, x, y) * 255f; return $"{v.X:F0} {v.Y:F0} {v.Z:F0} {v.W:F0}"; }
        string Floats(GpuTexture g) { var v = t.ReadPixel(g, x, y); return $"{v.X:G4} {v.Y:G4} {v.Z:G4} {v.W:G4}"; }

        text.AppendLine($"albedo (bytes)  {Bytes(t.GBuffer[1])}");
        text.AppendLine($"normal (bytes)  {Bytes(t.GBuffer[3])}");
        text.AppendLine($"emission  {Floats(t.GBuffer[5])}");
        text.AppendLine($"linear depth  {Floats(t.LinearDepth)}");
        text.AppendLine($"pre-shadow  {Floats(t.PreShadow)}");
        text.AppendLine($"pre-misc  {Floats(t.PreMisc)}");
        text.AppendLine($"light field 0  {Floats(t.LayerCopy(t.FieldLightPrePassArray, 0))}");
        text.AppendLine($"light field 1  {Floats(t.LayerCopy(t.FieldLightPrePassArray, 1))}");
        text.AppendLine($"pass id  {Bytes(t.PassId)}");
        if (t.Stage(3) is { } chosen)
            text.AppendLine($"chosen pass output  {Floats(chosen)}");
        text.AppendLine($"HDR frame  {Floats(t.Final)}");
    }

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
        for (uint i = 0; i < 2; i++)
        {
            Span<bool> indexed = stackalloc bool[4];
            gl.GetBoolean(GLEnum.ColorWritemask, i, indexed);
            text.AppendLine($"  indexed mask {i}: {indexed[0]} {indexed[1]} {indexed[2]} {indexed[3]}");
        }
    }

    static string Slot(byte[] block, int slot) => block.Length < (slot + 1) * 16
        ? "(out of range)"
        : string.Join(' ', Enumerable.Range(0, 4).Select(i => BitConverter.ToSingle(block, slot * 16 + i * 4).ToString("G5")));
}
