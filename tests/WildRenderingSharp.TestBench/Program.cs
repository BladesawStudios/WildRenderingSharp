using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using WildRenderingSharp;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Preparation;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.TestBench;

// Prepares one actor from a romfs and renders it to a PNG through the real GL pipeline.
//
//   WildRenderingSharp.TestBench --game totk|botw --romfs <dir> --actor <name> [--cache <dir>] [--out <png>]
//                                [--size <px>] [--background sky|color]
//
// Exit codes: 0 rendered, 1 failure, 2 bad command line, 3 game has no profile yet, 4 the image is blank.

var options = args.Chunk(2).Where(p => p.Length == 2 && p[0].StartsWith("--")).ToDictionary(p => p[0][2..], p => p[1]);
string Option(string key, string fallback) => options.TryGetValue(key, out var v) ? v : fallback;

if (!options.TryGetValue("romfs", out var romfs) || !options.TryGetValue("actor", out var actorName))
{
    Console.Error.WriteLine("usage: --game totk|botw --romfs <dir> --actor <name> [--cache <dir>] [--out <png>] [--size <px>] [--background sky|color]");
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

IModelPreparer preparer = new InProcessPreparer();
await preparer.EnsureSystemAssetsAsync(romfs, cache, Console.WriteLine);
string model = await preparer.PrepareAsync(new PrepareRequest(romfs, actorName, cache), Console.WriteLine);
Console.WriteLine($"prepared: {model}");

if (game != "totk")
{
    Console.Error.WriteLine($"The '{game}' preparation ran, but the renderer has no {game} profile yet, so there is nothing to draw it with.");
    return 3;
}

var window = Window.Create(WindowOptions.Default with
{
    IsVisible = false,
    Size = new(size, size),
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
});
window.Initialize();
using var gl = GL.GetApi(window.GLContext);
Console.WriteLine($"GL: {gl.GetStringS(StringName.Renderer)} / {gl.GetStringS(StringName.Version)}");

var renderer = new WildRenderer(gl, cache, romfs, initialWidth: size, initialHeight: size);
renderer.Lighting.Background = Option("background", "sky") == "color" ? BackgroundMode.Color : BackgroundMode.Sky;
renderer.AddActor(model);

var camera = new Camera();
var (center, radius) = renderer.FrameFor(camera);
camera.Target = center;
camera.Eye = center + SceneFramingCalculator.DefaultViewDirection * radius * 3.5f;

// Several frames: the sky bake, shadow cache and exposure probe settle over the first few.
for (int i = 0; i < 4; i++)
    renderer.Render(camera, size, size, 1f / 60f);

var errors = new List<GLEnum>();
for (var e = gl.GetError(); e != GLEnum.NoError; e = gl.GetError())
    errors.Add(e);

byte[] rgba = renderer.View.ReadOutputRgba8();
PngWriter.Write(outPath, rgba, size, size);

int distinct = new HashSet<uint>(Enumerable.Range(0, rgba.Length / 4).Select(i => BitConverter.ToUInt32(rgba, i * 4))).Count;
double luma = Enumerable.Range(0, rgba.Length / 4).Average(i => (rgba[i * 4] + rgba[i * 4 + 1] + rgba[i * 4 + 2]) / 765.0);
Console.WriteLine($"wrote {outPath}: {distinct} distinct colours, mean luminance {luma:F3}, GL errors: {(errors.Count == 0 ? "none" : string.Join(", ", errors))}");

renderer.Dispose();
window.Dispose();
return distinct < 64 || errors.Count > 0 ? 4 : 0;
