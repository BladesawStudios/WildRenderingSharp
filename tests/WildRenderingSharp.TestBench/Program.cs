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
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Rom.Games;
using WildRenderingSharp.Storage;
using WildRenderingSharp.TestBench;

// Prepares one actor from a romfs and renders it to a PNG through the real GL pipeline; the options are in the usage text.
// Exit codes: 0 rendered, 1 failure, 2 bad command line, 4 the image is nearly blank, 5 GL reported errors.

var options = args.Chunk(2).Where(p => p.Length == 2 && p[0].StartsWith("--")).ToDictionary(p => p[0][2..], p => p[1]);
string Option(string key, string fallback) => options.TryGetValue(key, out var v) ? v : fallback;

if (!options.TryGetValue("romfs", out var romfs) || !options.TryGetValue("actor", out var actorName))
{
    Console.Error.WriteLine("usage: --game totk|botw --romfs <dir> --actor <name> [--cache <dir>] [--out <png>] [--size <px>] " +
        "[--background sky|color] [--sun <radians>] [--azimuth <radians>] [--lookup <degrees>] [--exposure <x>] [--probe 1] [--frames <n>] [--instances <n> [--spacing <x>]] " +
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
var mixModels = new List<string>();
if (options.TryGetValue("mix", out var mixNames))
    foreach (string name in mixNames.Split(',', StringSplitOptions.RemoveEmptyEntries))
        mixModels.Add(await preparer.PrepareAsync(new PrepareRequest(romfs, name, cache), Console.WriteLine));
string? behindModel = options.TryGetValue("behind", out var behindActor)
    ? await preparer.PrepareAsync(new PrepareRequest(romfs, behindActor, cache), Console.WriteLine) : null;
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

if (options.TryGetValue("bindless-limit", out var limitText))
{
    // How many bindless handles the driver will keep resident at once, and whether each stays so.
    var bindless = gl.TryGetExtension(out Silk.NET.OpenGL.Extensions.ARB.ArbBindlessTexture ext) ? ext : throw new InvalidOperationException("no bindless");
    int count = int.Parse(limitText), notResident = 0, firstFail = -1, glFaults = 0;
    var handles = new ulong[count];
    for (int i = 0; i < count; i++)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(Silk.NET.OpenGL.TextureTarget.Texture2D, tex);
        gl.TexImage2D(Silk.NET.OpenGL.TextureTarget.Texture2D, 0, Silk.NET.OpenGL.InternalFormat.Rgba8, 16, 16, 0, Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, (ReadOnlySpan<byte>)new byte[16 * 16 * 4]);
        gl.TexParameter(Silk.NET.OpenGL.TextureTarget.Texture2D, Silk.NET.OpenGL.TextureParameterName.TextureMinFilter, (int)Silk.NET.OpenGL.TextureMinFilter.Linear);
        handles[i] = bindless.GetTextureHandle(tex);
        bindless.MakeTextureHandleResident(handles[i]);
        if (gl.GetError() != Silk.NET.OpenGL.GLEnum.NoError) { glFaults++; if (firstFail < 0) firstFail = i; }
    }
    for (int i = 0; i < count; i++)
        if (!bindless.IsTextureHandleResident(handles[i])) { notResident++; if (firstFail < 0) firstFail = i; }
    Console.WriteLine($"bindless handles: {count} made, {notResident} not resident, {glFaults} GL errors, first problem at {firstFail}");
    return 0;
}

if (options.ContainsKey("check-merged"))
    return MergedCheck.Run(gl, cache, int.Parse(options["check-merged"]));

if (options.ContainsKey("check-water-shader"))
{
    // Compiles the game's terrain water fragment stage as the renderer patches it, for a host-less check of the patch.
    var patch = typeof(WildRenderer).Assembly.GetType("WildRenderingSharp.Profiles.Totk.Terrain.TerrainShading")!
        .GetMethod("PatchedWaterFragment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    string source = (string)patch.Invoke(null, [Path.Combine(cache.Root, "_shaders")])!;
    uint shader = gl.CreateShader(ShaderType.FragmentShader);
    gl.ShaderSource(shader, source);
    gl.CompileShader(shader);
    gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
    Console.WriteLine(compiled != 0 ? "water fragment: compiles" : "water fragment: FAILED " + gl.GetShaderInfoLog(shader));
    return compiled != 0 ? 0 : 1;
}

using var rom = TotkRom.Open(romfs);
var renderer = new WildRenderer(gl, cache, rom, initialWidth: size, initialHeight: size);
renderer.Lighting.Background = Option("background", "sky") == "color" ? BackgroundMode.Color : BackgroundMode.Sky;
int instanceCount = int.Parse(Option("instances", "0"));
if (instanceCount == 0 && mixModels.Count == 0)
{
    var actor = renderer.AddActor(model);
    // Animations held at a frame, as "m:<name>=<frame>" (shader parameters) or "t:<name>=<frame>" (texture patterns), comma separated.
    foreach (string held in Option("hold", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] part = held[2..].Split('=');
        string file = Path.Combine(cache.ModelDirectory(model), $"{model}.{part[0]}.{(held[0] == 'm' ? "matanim" : "texpat")}.json");
        if (!File.Exists(file)) { Console.WriteLine($"hold: no {file}"); continue; }
        if (held[0] == 'm') actor.ShaderParam.Add(WildRenderingSharp.Animation.Clips.MaterialAnimManifest.Load(file)).Seek(float.Parse(part[1]));
        else actor.TexturePattern.Add(WildRenderingSharp.Animation.Clips.TexturePatternAnimManifest.Load(file)).Seek(float.Parse(part[1]));
    }
}
if (game == "totk")
    TotkBenchOptions.Apply(renderer, options);
if (options.TryGetValue("bloom", out var bloom))
    renderer.Lighting.BloomIntensity = float.Parse(bloom);
if (options.TryGetValue("azimuth", out var azimuth))
    renderer.Lighting.SunAzimuth = float.Parse(azimuth);
if (options.TryGetValue("sun", out var sun))
    renderer.Lighting.SunElevation = float.Parse(sun);
if (options.TryGetValue("load-bench", out var loadBench))
{
    // How long a host's loader thread takes per model: the prepared models of the cache, loaded one after another as World view's loader loads them.
    renderer.CompactModelVertices = true;
    renderer.TextureMaxSize = 512;
    var names = Directory.EnumerateDirectories(cache.Root).Select(Path.GetFileName).Where(n => n is not null && !n.StartsWith('_') && cache.IsPrepared(n)).Order().ToList();
    int count = Math.Min(int.Parse(loadBench), names.Count);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < count; i++)
        renderer.LoadModelOnWorker(names[i]!);
    gl.Finish();
    Console.WriteLine($"loaded {count} models in {clock.Elapsed.TotalSeconds:F1} s = {count / clock.Elapsed.TotalSeconds:F1} models/s ({clock.Elapsed.TotalMilliseconds / count:F0} ms each)");
    return 0;
}

var camera = new Camera { FovDegrees = 38f };
var (center, radius) = mixModels.Count > 0
    ? InstanceGrid.AddRow(renderer, [model, .. mixModels], Math.Max(instanceCount, 1), float.Parse(Option("cell", "4")))
    : instanceCount > 0
    ? InstanceGrid.Add(renderer, model, instanceCount, float.Parse(Option("spacing", "0.5")), ParseOrigin(Option("world", "0,0,0")), Option("bake", "0") is var bakeText && bakeText != "0" ? Convert.ToUInt64(bakeText.Replace("0x", ""), 16) : 0UL)
    : renderer.FrameFor(camera);
if (instanceCount > 0 || mixModels.Count > 0)
{
    var framing = SceneFramingCalculator.ForModelRadius(radius);
    (camera.NearPlane, camera.FarPlane) = (framing.Near, framing.Far);
}
camera.Target = center;
camera.Eye = center + SceneFramingCalculator.DefaultViewDirection * radius * 3.5f;

// A second actor behind the first, for what reads the lit scene behind it: its own centre moved away from the camera and down.
if (behindModel is not null)
{
    var behind = renderer.LoadModel(behindModel);
    var away = -SceneFramingCalculator.DefaultViewDirection;
    var at = center + away * radius * float.Parse(Option("behind-distance", "3")) + Vector3.UnitY * float.Parse(Option("behind-drop", "0"));
    var batch = renderer.AddInstances(behind, [[new Vector4(1, 0, 0, at.X), new Vector4(0, 1, 0, at.Y), new Vector4(0, 0, 1, at.Z)]]);
    batch.ShowAll();
}


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

if (options.ContainsKey("cascades"))
{
    // The host's shadow cascades, as a map draws them: each four times the reach of the last, centred half a reach ahead of the camera.
    var forward = Vector3.Normalize(camera.Target - camera.Eye);
    renderer.ShadowCascades = new[] { 64f, 256f, 1024f, 4096f }
        .Select(reach => new ShadowFocus(camera.Eye + forward * reach * 0.5f, reach)).ToArray();
}

if (options.TryGetValue("view", out var view))
    renderer.View.Mode = Enum.Parse<WildRenderingSharp.Hosting.Views.SceneViewMode>(view, ignoreCase: true);

// Several frames: the sky bake, shadow cache and exposure probe settle over the first few.
for (int i = 0; i < 4; i++)
    renderer.Render(camera, size, size, 1f / 60f);

if (options.TryGetValue("frames", out var frames))
    Timing.Report(renderer, gl, camera, size, int.Parse(frames));

var errors = new List<GLEnum>();
for (var e = gl.GetError(); e != GLEnum.NoError; e = gl.GetError())
    errors.Add(e);

if (options.ContainsKey("probe"))
    foreach (float v in new[] { 0.05f, 0.2f, 0.4f, 0.6f, 0.62f, 0.64f, 0.66f, 0.68f, 0.7f, 0.75f })
        Console.WriteLine($"HDR v={v:F2}: {renderer.View.ProbeHdr(new Vector2(float.Parse(Option("probe-x", "0.85")), v))}");

if (options.ContainsKey("centre"))
    Console.WriteLine(renderer.View.ProbeCentre());

byte[] rgba = renderer.View.ReadOutputRgba8();
PngWriter.WriteRgba(outPath, size, size, rgba);

var draws = renderer.Pipeline.DrawCounts;
Console.WriteLine($"actor draws: {draws.Calls} calls with {draws.Commands} commands, {draws.TextureBinds} texture binds, {draws.ProgramSwitches} program switches");

int distinct = new HashSet<uint>(Enumerable.Range(0, rgba.Length / 4).Select(i => BitConverter.ToUInt32(rgba, i * 4))).Count;
double luma = Enumerable.Range(0, rgba.Length / 4).Average(i => (rgba[i * 4] + rgba[i * 4 + 1] + rgba[i * 4 + 2]) / 765.0);
Console.WriteLine($"wrote {outPath}: {distinct} distinct colours, mean luminance {luma:F3}, GL errors: {(errors.Count == 0 ? "none" : string.Join(", ", errors))}");

renderer.Dispose();
window.Dispose();
return errors.Count > 0 ? 5 : distinct < 64 ? 4 : 0;

static Vector3 ParseOrigin(string text)
{
    var parts = text.Split(',').Select(float.Parse).ToArray();
    return new Vector3(parts[0], parts[1], parts[2]);
}
