using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Profiles.Totk.Effects;

namespace WildRenderingSharp.Tests;

public class ParticleBlocksTests
{
    static readonly ParticleEmitterManifest Emitter = new(200, [10f, 8f, 10f], [0f, -0.3f, 0f], [1f, 1f, 1f], [1f, 1f, 1f, 0.5f], 0, 2, 102, 480, false, [3f, 3f, 3f], 200f);

    [Fact]
    public void Rebind_moves_the_five_blocks_and_leaves_the_others()
    {
        const string Source = "layout (binding = 0, std140) uniform _support_buffer\n{\n}\n"
            + "layout (binding = 5, std140) uniform _NnVfx2ViewParam\n{\n}\n"
            + "layout (binding = 7, std140) uniform _NnVfx2EmitterDynamicParam\n{\n}\n"
            + "layout (binding = 6, std140) uniform _sysEmitterStaticUniformBlock\n{\n}\n"
            + "layout (binding = 12, std140) uniform _sysCustomShaderUniformBlock1\n{\n}\n"
            + "layout (binding = 9, std140) uniform _sysEmitterPluginUniformBlock\n{\n}\n";

        string rebound = ParticleBlocks.Rebind(Source);

        Assert.Contains("binding = 0, std140) uniform _support_buffer", rebound);
        Assert.Contains("binding = 31, std140) uniform _NnVfx2ViewParam", rebound);
        Assert.Contains("binding = 32, std140) uniform _NnVfx2EmitterDynamicParam", rebound);
        Assert.Contains("binding = 33, std140) uniform _sysEmitterStaticUniformBlock", rebound);
        Assert.Contains("binding = 34, std140) uniform _sysCustomShaderUniformBlock1", rebound);
        Assert.Contains("binding = 35, std140) uniform _sysEmitterPluginUniformBlock", rebound);
    }

    [Fact]
    public void Dynamic_block_holds_the_clock_and_the_emitter_at_its_position()
    {
        var block = ParticleBlocks.Dynamic(Emitter, new Vector3(1f, 2f, 3f), 100f, "test");
        ReadOnlySpan<float> rows = MemoryMarshal.Cast<byte, float>(block.Bytes.Span);

        // The clock is half way through the particle's life, and each transform row ends in its translation.
        Assert.Equal(100f, rows[2 * 4]);
        Assert.Equal([1f, 0f, 0f, 1f], rows.Slice(4 * 4, 4).ToArray());
        Assert.Equal([0f, 1f, 0f, 2f], rows.Slice(5 * 4, 4).ToArray());
        Assert.Equal([0f, 0f, 1f, 3f], rows.Slice(6 * 4, 4).ToArray());
        Assert.Equal(rows.Slice(4 * 4, 16).ToArray(), rows.Slice(8 * 4, 16).ToArray());
    }

    [Fact]
    public void Custom_block_weighs_the_colour_by_one()
    {
        ReadOnlySpan<float> rows = MemoryMarshal.Cast<byte, float>(ParticleBlocks.Custom("test").Bytes.Span);

        Assert.Equal([1f, 1f, 1f, 1f], rows.Slice(33 * 4, 4).ToArray());
        Assert.Equal(0f, rows[49 * 4]);
    }

    [Fact]
    public void Area_loop_block_puts_the_box_ahead_of_the_camera()
    {
        // The main field's added fog: a box 5 by 4 by 5 at (0, 1.5, -6) from the camera, which it follows.
        float[] payload = new float[19];
        payload[4] = 5f; payload[5] = 4f; payload[6] = 5f;
        payload[8] = 0f; payload[9] = 1.5f; payload[10] = -6f;
        payload[12] = 0.3f; payload[13] = 0.3f; payload[14] = 0.3f;
        payload[15] = 1f;
        var loop = new AreaLoop(MemoryMarshal.AsBytes(payload.AsSpan()).ToArray());

        float[] rows = loop.Block(Matrix4x4.Identity, new Vector3(10f, 20f, 30f), Matrix4x4.Identity, 0);

        Assert.Equal(1, loop.RepeatCount);
        Assert.Equal(1f, rows[3]);
        Assert.Equal([10f, 21.5f, 24f, 1f], rows.Skip(5 * 4).Take(4).ToArray());
        Assert.Equal([5f, 4f, 5f, 0f], rows.Skip(11 * 4).Take(4).ToArray());
    }

    [Fact]
    public void Plugin_block_without_an_area_loop_is_all_zero()
    {
        ReadOnlySpan<float> rows = MemoryMarshal.Cast<byte, float>(ParticleBlocks.Plugin(null, "test").Bytes.Span);

        Assert.All(rows.ToArray(), value => Assert.Equal(0f, value));
    }
}
