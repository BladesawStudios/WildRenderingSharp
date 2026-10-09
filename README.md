# WildRenderingSharp

A C# OpenGL rendering library for the Wild Era Zelda games.

The renderer is built around game profiles. TotK is the full one; *Breath of the Wild* draws its G-buffer with the game's own programs and a placeholder resolve (see [docs/architecture.md](docs/architecture.md)). Everything below is TotK.


## Using it from a tool

```csharp
using WildRenderingSharp;
using WildRenderingSharp.Hosting.Preparers;
using WildRenderingSharp.Rom.Games;
using WildRenderingSharp.Storage;

// Off the GL thread: shared assets (cheap after the first run), then the actor.
IModelPreparer preparer = new OutOfProcessPreparer();      // or new InProcessPreparer()
await preparer.EnsureSystemAssetsAsync(romfs, CacheLayout.Default);
string model = await preparer.PrepareAsync(new PrepareRequest(romfs, "Npc_Zelda", CacheLayout.Default));

// On the GL thread (a 4.5-capable context).
using var rom = TotkRom.Open(romfs);
var renderer = new WildRenderer(gl, CacheLayout.Default, rom);
var actor = renderer.AddActor(model);

// Each frame.
renderer.Advance(deltaSeconds);
uint? texture = renderer.Render(camera, width, height, deltaSeconds);
// Draw `texture` - bottom row first, the GL way. renderer.View.ImGuiUv gives ImGui's uv0/uv1.
```

- `DeferredPipeline` - the pipeline; `RenderFrame(FrameRequest)` draws one frame.
- `SceneView` - an offscreen view of a pipeline: supersampling, FXAA, the diagnostic view modes
  (albedo, normal, shadow, AO, pass ID, raw HDR), readback for PNG/HDR export. Several views can
  share one pipeline (`ownTargets: true` for a secondary one).
- `RenderActor` - a placed model with its own animation channels. Not sealed; hang your own editor
  state off it, and override `ModifiesPose`/`ModifyPose` to apply your own physics to its pose.
  `ExternalPose` lets a host that already poses the same skeleton supply its own bone matrices;
  `TransformOverride` takes a host's own placement matrix.
- `RenderEnvironment` - palettes, sky/cloud postfx and colour grade from the romfs.
- `LightingContext` - exposure, palette, sun, background mode, sky/cloud/flare switches.
- `WildRenderer.Totk` - the TotK profile's live settings (`TotkSettings`): palette, atmosphere, sun and moon, lens flare, and the clouds
  (`CloudWeatherSet` 0-2, `CloudLayerEnabled`, `CloudWind`, `AnimateClouds`, `CloudBrightness`).
- `Log.Sink` - the library writes its messages to the console unless a host sets this to take them (or to null to silence them).
- `GLHostState` - see below.
- The world is right-handed and Y-up, the games' own: cameras, actor placements and the sun are given in it as they are.
- `Imaging.PngWriter` / `Imaging.HdrWriter` - dependency-free export.

### Build integration

Reference `src/WildRenderingSharp/WildRenderingSharp.csproj` (and the Preparation project, for
in-process preparation), and import the targets file:

```xml
<ProjectReference Include="vendor\WildRenderingSharp\src\WildRenderingSharp\WildRenderingSharp.csproj" />
<Import Project="vendor\WildRenderingSharp\build\WildRenderingSharp.targets" />
```

It builds the out-of-process preparer into a folder beside your executable, on build and on publish:

- `wrs-prepare\` - the out-of-process preparer. Set `<WrsOutOfProcessPreparer>false</WrsOutOfProcessPreparer>`
  if you prepare in-process and do not need it.

## Architecture

A game-neutral core plus one profile per game; see [docs/architecture.md](docs/architecture.md) for the layout, the frame and how to add a game.

## Building

```bash
git clone --recursive https://github.com/BladesawStudios/WildRenderingSharp.git
dotnet build WildRenderingSharp.slnx
```

The dependencies are all submodules under `vendor/` and all managed (ShaderLibrary, AampSharp, McSharp, BntxSharp, TxtgSharp with its TexSharp), so
preparation needs no Windows binaries; it has not been run on Linux yet. Everything targets net10.0.

`WildRenderingSharp.Preparation` on its own:

```bash
WildRenderingSharp.Preparation ensure-system --romfs <romfs> [--cache <dir>]
WildRenderingSharp.Preparation prepare --romfs <romfs> --actor <name> [--cache <dir>] [--game totk|botw] [--mod <romfs dir>]... [--no-anims] [--force]
```

A headless GL bench renders one actor to a PNG for checking changes without a host:

```bash
dotnet run --project tests/WildRenderingSharp.TestBench -- --game totk --romfs <romfs> --actor Npc_Zelda_AncientHyrule --out zelda.png
```

## Debugging switches

Environment variables (the older `MARROW_*` names still work):

| Variable | Effect |
| --- | --- |
| `WRS_GL_TRACE=1` | Check for GL errors after every pass, not just once per frame, to find which pass raised one. |
| `WRS_SKY_ORDERS=<n>` | Scattering orders the sky precompute bakes (default 6). |
| `WRS_SKY_DUMP=<file>` | Write the baked inscatter table (raw floats) to a file. |
| `WRS_CLOUD_DUMP=<file>` | Write the cloud pass's `Common` uniform block to a file. |
