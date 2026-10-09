using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using WildRenderingSharp;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Hosting.Preparers;
using WildRenderingSharp.Imaging;
using WildRenderingSharp.Preparation;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Rendering.Cameras;
using WildRenderingSharp.Rendering.Lighting;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Rom.Games;
using WildRenderingSharp.Storage;
using WildRenderingSharp.TestBench;

// Prepares one actor from a romfs and renders it to a PNG through the real GL pipeline.
//
//   WildRenderingSharp.TestBench --game totk|botw --romfs <dir> --actor <name> [--cache <dir>] [--out <png>]
//                                [--size <px>] [--background sky|color] [--sun <elevation radians>]
//                                [--azimuth <radians>] [--lookup <degrees>] [--exposure <x>] [--probe 1]
//   with --game totk: see TotkBenchOptions.Usage
//
// Exit codes: 0 rendered, 1 failure, 2 bad command line, 4 the image is nearly blank, 5 GL reported errors.

var options = args.Chunk(2).Where(p => p.Length == 2 && p[0].StartsWith("--")).ToDictionary(p => p[0][2..], p => p[1]);
string Option(string key, string fallback) => options.TryGetValue(key, out var v) ? v : fallback;

if (!options.TryGetValue("romfs", out var romfs) || !options.TryGetValue("actor", out var actorName))
{
    Console.Error.WriteLine("usage: --game totk|botw --romfs <dir> --actor <name> [--cache <dir>] [--out <png>] [--size <px>] " +
        "[--background sky|color] [--sun <radians>] [--azimuth <radians>] [--lookup <degrees>] [--exposure <x>] [--probe 1] " +
        (Option("game", "totk") == "totk" ? TotkBenchOptions.Usage : ""));
    return 2;
}

string game = Option("game", "totk").ToLowerInvariant();
int size = int.Parse(Option("size", "768"));
string outPath = Path.GetFullPath(Option("out", $"{game}_{actorName}.png"));
var cache = options.TryGetValue("cache", out var cacheDir) ? new CacheLayout(cacheDir) : CacheLayout.Default;

if (!Directory.Exists(romfs))
{
    Console.Error.WriteLine($"romfs '{romfs}' does not exist.");
    return 2;
}

if (game == "botw")
    return BotwBench.Run(romfs, actorName, cache, size, outPath, options);

IModelPreparer preparer = new InProcessPreparer();
await preparer.EnsureSystemAssetsAsync(romfs, cache, Console.WriteLine);
string model = await preparer.PrepareAsync(new PrepareRequest(romfs, actorName, cache), Console.WriteLine);
Console.WriteLine($"prepared: {model}");

var window = Window.Create(WindowOptions.Default with
{
    IsVisible = false,
    Size = new(size, size),
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
});
window.Initialize();
using var gl = GL.GetApi(window.GLContext);
Console.WriteLine($"GL: {gl.GetStringS(StringName.Renderer)} / {gl.GetStringS(StringName.Version)}");

using var rom = TotkRom.Open(romfs);
var renderer = new WildRenderer(gl, cache, rom, initialWidth: size, initialHeight: size);
renderer.Lighting.Background = Option("background", "sky") == "color" ? BackgroundMode.Color : BackgroundMode.Sky;
renderer.AddActor(model);
if (game == "totk")
    TotkBenchOptions.Apply(renderer, options);
if (options.TryGetValue("azimuth", out var azimuth))
    renderer.Lighting.SunAzimuth = float.Parse(azimuth);
if (options.TryGetValue("sun", out var sun))
    renderer.Lighting.SunElevation = float.Parse(sun);
var camera = new Camera { FovDegrees = 38f };
var (center, radius) = renderer.FrameFor(camera);
camera.Target = center;
camera.Eye = center + SceneFramingCalculator.DefaultViewDirection * radius * 3.5f;

if (options.TryGetValue("lookup", out var lookup))
{
    var flat = Vector3.Normalize(new Vector3(center.X - camera.Eye.X, 0, center.Z - camera.Eye.Z));
    float pitch = float.DegreesToRadians(float.Parse(lookup));
    camera.Target = camera.Eye + flat * MathF.Cos(pitch) * radius + Vector3.UnitY * MathF.Sin(pitch) * radius;
}
if (options.TryGetValue("altitude", out var altitude))
{
    var lift = Vector3.UnitY * float.Parse(altitude);
    camera.Eye += lift;
    camera.Target += lift;
}
if (options.TryGetValue("exposure", out var exposure))
    renderer.Lighting.Exposure = float.Parse(exposure);

// Several frames: the sky bake, shadow cache and exposure probe settle over the first few.
for (int i = 0; i < 4; i++)
    renderer.Render(camera, size, size, 1f / 60f);

var errors = new List<GLEnum>();
for (var e = gl.GetError(); e != GLEnum.NoError; e = gl.GetError())
    errors.Add(e);

if (options.ContainsKey("probe"))
    foreach (float v in new[] { 0.05f, 0.2f, 0.4f, 0.6f, 0.62f, 0.64f, 0.66f, 0.68f, 0.7f, 0.75f })
        Console.WriteLine($"HDR v={v:F2}: {renderer.View.ProbeHdr(new Vector2(float.Parse(Option("probe-x", "0.85")), v))}");

byte[] rgba = renderer.View.ReadOutputRgba8();
PngWriter.WriteRgba(outPath, size, size, rgba);

int distinct = new HashSet<uint>(Enumerable.Range(0, rgba.Length / 4).Select(i => BitConverter.ToUInt32(rgba, i * 4))).Count;
double luma = Enumerable.Range(0, rgba.Length / 4).Average(i => (rgba[i * 4] + rgba[i * 4 + 1] + rgba[i * 4 + 2]) / 765.0);
Console.WriteLine($"wrote {outPath}: {distinct} distinct colours, mean luminance {luma:F3}, GL errors: {(errors.Count == 0 ? "none" : string.Join(", ", errors))}");

renderer.Dispose();
window.Dispose();
return errors.Count > 0 ? 5 : distinct < 64 ? 4 : 0;
