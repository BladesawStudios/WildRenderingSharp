using System.Numerics;
using System.Text.RegularExpressions;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>
/// The uniform blocks a particle emitter's programs read: the view, the emitter's dynamic values, its static block, and the game's custom block.
/// They are bound at bindings of their own, which the programs are rewritten to read, so drawing an emitter displaces none of the frame's blocks.
/// </summary>
/// <remarks>
/// The view and dynamic blocks have the layouts the engine's particle system uploads: matrices as rows of a transposed matrix, the dynamic block's
/// first row the emitter colour, its second the emitter clock, its third the alpha and scale, then the emitter's transform with and without scale.
/// </remarks>
internal static partial class ParticleBlocks
{
    const uint ViewBinding = 31, DynamicBinding = 32, StaticBinding = 33, CustomBinding = 34, PluginBinding = 35;

    const int ViewRows = 32, DynamicRows = 12, CustomRows = 64, PluginRows = 16;
    const int ViewMatrix = 0, ProjectionMatrix = 4, BillboardMatrix = 24, EyeRow = 29, DepthRow = 30;
    const int ColorRow = 0, ClockRow = 2, ScaleRow = 3, TransformRow = 4, RigidTransformRow = 8;

    // The game's custom block, as the volume-mask program uses it: row 33 weighs the particle's colour, and when row 49's first value is not zero the vertex stage
    // fades the particle by a world texture (the sampler at unit 20, positioned by row 52) over the distance ramp of row 51.
    // TODO: the game fills this block from a callback this does not run, so the colour weights are one and the rest is zero, which leaves the world texture out.
    // Running the callback's logic would give the game's values.
    const int CustomColorRow = 33;

    static readonly Dictionary<string, uint> Bindings = new(StringComparer.Ordinal)
    {
        ["NnVfx2ViewParam"] = ViewBinding,
        ["NnVfx2EmitterDynamicParam"] = DynamicBinding,
        ["sysEmitterStaticUniformBlock"] = StaticBinding,
        ["sysCustomShaderUniformBlock1"] = CustomBinding,
        ["sysEmitterPluginUniformBlock"] = PluginBinding,
    };

    // Rewrites the bindings of the blocks a program declares to the ones above.
    public static string Rebind(string source) => BlockDeclaration().Replace(source, match =>
        Bindings.TryGetValue(match.Groups["name"].Value, out uint binding) ? $"binding = {binding}, std140) uniform _{match.Groups["name"].Value}" : match.Value);

    [GeneratedRegex(@"binding = \d+, std140\) uniform _(?<name>\w+)")]
    private static partial Regex BlockDeclaration();

    public static Ubo View(CameraData camera, string key)
    {
        var block = new UboWriter(new UboSpec("NnVfx2ViewParam", ViewBinding, ViewRows * 16));
        block.Set(new UboMatrix(ViewMatrix, 4), camera.View);
        block.Set(new UboMatrix(ProjectionMatrix, 4), camera.Proj);
        // The billboard matrix turns a particle to face the camera: the camera's orientation without its position, which is the eye's row beside it.
        var billboard = camera.ViewInv;
        billboard.Translation = Vector3.Zero;
        block.Set(new UboMatrix(BillboardMatrix, 4), billboard);
        block.Set(EyeRow, camera.ViewInv.Translation.X, camera.ViewInv.Translation.Y, camera.ViewInv.Translation.Z, 0f);
        // The scene's distance constants: with no scene to fade against they only keep a culled particle out of view.
        float inverseNear = 1f / camera.Near, inverseFar = 1f / camera.Far;
        block.Set(DepthRow, 0f, -inverseNear, -1f, -(inverseNear - inverseFar));
        return block.ToUbo(key);
    }

    public static Ubo Dynamic(ParticleEmitterManifest emitter, Vector3 position, float clock, string key)
    {
        var block = new UboWriter(new UboSpec("NnVfx2EmitterDynamicParam", DynamicBinding, DynamicRows * 16));
        block.Set(ColorRow, emitter.Color0[0], emitter.Color0[1], emitter.Color0[2], emitter.Color0[3]);
        block.Set(ColorRow + 1, emitter.Color0[0], emitter.Color0[1], emitter.Color0[2], emitter.Color0[3]);
        // The emitter's own fade and scale are one.
        block.Set(ClockRow, clock, 1f, 1f, 1f);
        block.Set(ScaleRow, 1f, 1f, 1f, 1f);
        block.Set(new UboMatrix(TransformRow, 4), Matrix4x4.CreateScale(emitter.Scale[0], emitter.Scale[1], emitter.Scale[2]) * Matrix4x4.CreateTranslation(position));
        block.Set(new UboMatrix(RigidTransformRow, 4), Matrix4x4.CreateTranslation(position));
        return block.ToUbo(key);
    }

    public static Ubo Static(ParticleEmitterAsset asset) =>
        new($"particle_static_{asset.Id}", new UboSpec("sysEmitterStaticUniformBlock", StaticBinding, asset.StaticBlock.Length), asset.StaticBlock);

    // The emitter plugin's block: the area loop's rows for one repeat, or all zero for an emitter with no plugin, whose particles stay where they are put.
    public static Ubo Plugin(float[]? rows, string key)
    {
        var block = new UboWriter(new UboSpec("sysEmitterPluginUniformBlock", PluginBinding, PluginRows * 16));
        for (int row = 0; row < (rows?.Length ?? 0) / 4; row++)
            block.Set(row, rows![row * 4], rows[(row * 4) + 1], rows[(row * 4) + 2], rows[(row * 4) + 3]);
        return block.ToUbo(key);
    }

    public static Ubo Custom(string key)
    {
        var block = new UboWriter(new UboSpec("sysCustomShaderUniformBlock1", CustomBinding, CustomRows * 16));
        block.Set(CustomColorRow, 1f, 1f, 1f, 1f);
        return block.ToUbo(key);
    }
}
