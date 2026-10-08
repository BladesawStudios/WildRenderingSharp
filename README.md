# WildRenderingSharp

A C# OpenGL rendering library for the Wild Era Zelda games.

*Breath of the Wild* is planned. The renderer is built around game profiles, and TotK is the only one so far; everything below is TotK.


## Using it from a tool

```csharp
using WildRenderingSharp;
using WildRenderingSharp.Hosting;

// Off the GL thread: shared assets (cheap after the first run), then the actor.
IModelPreparer preparer = new OutOfProcessPreparer();      // or new InProcessPreparer()
await preparer.EnsureSystemAssetsAsync(romfs, CacheLayout.Default);
string model = await preparer.PrepareAsync(new PrepareRequest(romfs, "Npc_Zelda", CacheLayout.Default));

// On the GL thread (a 4.5-capable context).
var renderer = new WildRenderer(gl, CacheLayout.Default, romfs);
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
- `GLHostState` - see below.
- `YUpWorld` - conversions for a Y-up host (the renderer's world is Z-up).
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

`WildRenderingSharp.Preparation` on its own:

```bash
WildRenderingSharp.Preparation ensure-system --romfs <romfs> [--cache <dir>]
WildRenderingSharp.Preparation prepare --romfs <romfs> --actor <name> [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]
```

## Debugging switches

Environment variables (the older `MARROW_*` names still work):

| Variable | Effect |
| --- | --- |
| `WRS_GL_TRACE=1` | Check for GL errors after every pass, not just once per frame, to find which pass raised one. |
| `WRS_SKY_ORDERS=<n>` | Scattering orders the sky precompute bakes (default 6). |
| `WRS_SKY_DUMP=<file>` | Write the baked inscatter table (raw floats) to a file. |
| `WRS_CLOUD_DUMP=<file>` | Write the cloud pass's `Common` uniform block to a file. |
