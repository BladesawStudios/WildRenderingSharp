using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Preparation.Botw;
using WildRenderingSharp.Profiles.Botw;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Scene;

namespace WildRenderingSharp.TestBench;

/// <summary>Prepares one BotW model from a Switch dump and draws it through the BotW profile to a PNG.</summary>
static class BotwBench
{
    public static int Run(string rom, string model, CacheLayout cache, int size, string outPath, IReadOnlyDictionary<string, string> options)
    {
        BotwModelPreparer.PrepareIfNeeded(rom, model, cache, Console.WriteLine, force: options.ContainsKey("force"));

        var window = Window.Create(WindowOptions.Default with
        {
            IsVisible = false,
            Size = new(size, size),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
        });
        window.Initialize();
        using var gl = GL.GetApi(window.GLContext);
        Console.WriteLine($"GL: {gl.GetStringS(StringName.Renderer)} / {gl.GetStringS(StringName.Version)}");

        using var host = GLHostState.Enter(gl);
        var pipeline = new DeferredPipeline(gl, cache.Root, cache.Shaders, size, size, profile: new BotwProfile());
        var view = new SceneView(gl, pipeline);
        var textures = new ExternalTextures(gl);
        var loader = new ModelLoader(gl, pipeline.Programs, cache.ModelDirectory(model), textures) { SharedTextures = new SharedTextures(gl) };
        var actor = new RenderActor { Model = loader.Load(model, enableKnownDecompilerCorrections: false), ModelName = model, Name = model };
        pipeline.SetScene([actor.Model]);

        var environment = new BotwEnvironment();
        var lighting = new LightingContext();
        if (options.TryGetValue("exposure", out var exposure))
            lighting.Exposure = float.Parse(exposure);

        var camera = new Camera();
        var (center, radius) = RenderActor.CombinedBounds([actor]);
        var framing = SceneFramingCalculator.ForModelRadius(radius);
        camera.NearPlane = framing.Near;
        camera.FarPlane = framing.Far;
        camera.Target = center;
        var direction = SceneFramingCalculator.DefaultViewDirection;
        if (options.TryGetValue("yaw", out var yaw))
            direction = System.Numerics.Vector3.Transform(direction, System.Numerics.Matrix4x4.CreateRotationZ(float.DegreesToRadians(float.Parse(yaw))));
        float distance = options.TryGetValue("distance", out var d) ? float.Parse(d) : 3.5f;
        camera.Eye = center + direction * radius * distance;
        if (options.TryGetValue("height", out var h))
            camera.Target += System.Numerics.Vector3.UnitZ * radius * float.Parse(h);
        camera.Eye += camera.Target - center;

        for (int i = 0; i < 3; i++)
        {
            var inputs = RenderActor.BuildRenderInputs([actor], 1f / 60f, (ulong)i + 1);
            view.Render(new FrameRequest(camera, lighting, environment, inputs, framing.AoRadius, framing.ShadowBias), size, size);
        }

        var errors = new List<GLEnum>();
        for (var e = gl.GetError(); e != GLEnum.NoError; e = gl.GetError())
            errors.Add(e);

        byte[] rgba = view.ReadOutputRgba8();
        PngWriter.Write(outPath, rgba, size, size);
        int distinct = new HashSet<uint>(Enumerable.Range(0, rgba.Length / 4).Select(i => BitConverter.ToUInt32(rgba, i * 4))).Count;
        Console.WriteLine($"wrote {outPath}: {distinct} distinct colours, GL errors: {(errors.Count == 0 ? "none" : string.Join(", ", errors))}");
        return distinct < 64 || errors.Count > 0 ? 4 : 0;
    }
}
