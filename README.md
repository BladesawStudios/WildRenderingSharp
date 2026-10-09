# WildRenderingSharp

A C# OpenGL rendering library for the Wild Era Zelda games.

The renderer is built around game profiles. TotK is the full one; *Breath of the Wild* draws its G-buffer with the game's own programs and a placeholder resolve. Everything below is TotK.

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

- `WildRenderer` - the whole live renderer: pipeline, environment, placed actors and a view. It enters `GLHostState` around its own work, which puts the
  GL context into the state the renderer expects and restores the host's afterwards.
- `RenderActor` - a placed model with its own animation channels. Not sealed: override `ModifyPose` to apply your own physics, or set `ExternalPose` and
  `TransformOverride` to supply your own bones and placement.
- `SceneView` - the offscreen view: supersampling, FXAA, diagnostic view modes (albedo, normal, shadow, AO, pass ID, raw HDR) and PNG/HDR readback.
- `WildRenderer.Totk` (`TotkSettings`) - palette, atmosphere, sun and moon, lens flare and cloud settings; `LightingContext` - exposure, sun and background.
- `Log.Sink` - messages go to the console unless a host sets this to take them, or to null to silence them.
- The world is right-handed and Y-up, the games' own: cameras, placements and the sun are given in it as they are.

### Build integration

Reference `src/WildRenderingSharp/WildRenderingSharp.csproj` (and the Preparation project, for
in-process preparation), and import the targets file:

```xml
<ProjectReference Include="vendor\WildRenderingSharp\src\WildRenderingSharp\WildRenderingSharp.csproj" />
<Import Project="vendor\WildRenderingSharp\build\WildRenderingSharp.targets" />
```

It builds the out-of-process preparer into `wrs-prepare\` beside your executable, on build and on publish. Set
`<WrsOutOfProcessPreparer>false</WrsOutOfProcessPreparer>` if you prepare in-process.

## Architecture

A game-neutral core plus one profile per game; see [docs/architecture.md](docs/architecture.md) for the layout, the frame and how to add a game.

## Building

```bash
git clone --recursive https://github.com/BladesawStudios/WildRenderingSharp.git
dotnet build WildRenderingSharp.slnx
```

The dependencies are managed submodules under `vendor/`, so preparation needs no Windows binaries (it has not been run on Linux yet). Everything targets net10.0.

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

Environment variables:

| Variable | Effect |
| --- | --- |
| `WRS_GL_TRACE=1` | Check for GL errors after every pass, not just once per frame, to find which pass raised one. |
| `WRS_SKY_ORDERS=<n>` | Scattering orders the sky precompute bakes (default 6). |
| `WRS_SKY_DUMP=<file>` | Write the baked inscatter table (raw floats) to a file. |
| `WRS_CLOUD_DUMP=<file>` | Write the cloud pass's `Common` uniform block to a file. |
