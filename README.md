# WildRenderingSharp

A C# OpenGL rendering library for the Wild Era Zelda games.

It draws *Tears of the Kingdom* models the way the game does: with the game's **own shaders**,
decompiled out of its compiled shader binaries, fed the uniform blocks, textures and program
variants the game would feed them, through a deferred pipeline modelled on the game's own - G-buffer,
the shared deferred-resolve passes (`chara_skin`, `chara_hair`, `chara_eye`, ...), screen-space
shadow and AO, the forward pass for blended materials, the real `agl` sky (Bruneton precomputed
scattering), cloud dome, sun/moon sprites, lens flare, bloom, `agl_hdr_compose` and the game's own
colour grade. Actors animate (skeletal, material, texture-pattern and texture-SRT clips).

Physics is not part of it. Havok Cloth and Phive Helper Bones are their own libraries
([HkxSimSharp](https://github.com/BladesawStudios/HkxSimSharp), with
[HkxSharp](https://github.com/BladesawStudios/HkxSharp) and
[PhiveSharp](https://github.com/BladesawStudios/PhiveSharp) to read the data, and
[HkxHbSharp](https://github.com/BladesawStudios/HkxHbSharp)); a host that runs them hands the bones
they drive to the renderer through `RenderActor.ModifyPose`, or poses the whole skeleton itself and
passes it as `RenderActor.ExternalPose`.

It renders offscreen into an ordinary texture, so any tool can show it however it likes - an ImGui
image, a blit into its own framebuffer, or a file.

*Breath of the Wild* is planned; the shader profile has a stub for it. Everything below is TotK.

## Layout

| Project | What it is |
| --- | --- |
| `src/WildRenderingSharp` | The live renderer. Loads prepared models, runs the pipeline, evaluates animation and physics, and hosts it all (`WildRenderer`, `SceneView`, `RenderActor`). Parses no BFRES/BFSHA itself. |
| `src/WildRenderingSharp.AampReader` | AAMP parsing, loaded into its own `AssemblyLoadContext` at runtime - never referenced. |
| `src/WildRenderingSharp.Preparation` | The offline half: turns an actor in the user's romfs into the cache the renderer reads (and copies its `.bphcl`/`.bphhb` physics files beside it, for a host that simulates them). A library *and* an executable. |
| `vendor/ShaderLibrary` | Submodule. BFRES/BFSHA parsing and Tegra shader decompilation, used by the preparer. |
| `vendor/MeshCodec/meshcodec_cli.exe` | MeshCodec's prebuilt decoder for `.bfres.mc`. |
| `docs/` | Reverse-engineering notes: recovered uniform blocks, the sky decode, open questions. |

## Everything happens twice: offline, then live

**Offline**, once per model: `WildRenderingSharp.Preparation` exports geometry, textures, skeleton
and animations, builds each material's `gsys_material` block, resolves which compiled shader program
variant the game would pick for every shape, and decompiles those programs to GLSL - all into a
cache directory (`CacheLayout`, `%AppData%\WildRenderingSharp\cache` by default). It also builds the
shared assets every model needs once (system shaders, the deferred-resolve materials, sky LUT,
cloud masks, sun and moon sprites).

**Live**, every frame: `WildRenderingSharp` reads that cache - a manifest JSON, raw buffers, the
already-decompiled shaders - and draws. No BFRES or BFSHA is parsed at runtime.

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

The pieces underneath are public for a tool that wants its own arrangement:

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

### Choosing a preparer

Preparation runs ShaderLibrary, which carries its **own** vendored BfresLibrary build and patches
it at runtime. A tool that already loads a different BfresLibrary build (for instance one built from
source) cannot load ShaderLibrary's into the same process - the assemblies share a name. Such a tool
uses `OutOfProcessPreparer`, which runs the preparer as a child process. A tool with no clash can
reference `WildRenderingSharp.Preparation` directly and use `InProcessPreparer`.

### Build integration

Reference `src/WildRenderingSharp/WildRenderingSharp.csproj` (and the Preparation project, for
in-process preparation), and import the targets file:

```xml
<ProjectReference Include="vendor\WildRenderingSharp\src\WildRenderingSharp\WildRenderingSharp.csproj" />
<Import Project="vendor\WildRenderingSharp\build\WildRenderingSharp.targets" />
```

It builds two helpers into folders beside your executable, on build and on publish:

- `aampreader\` - the isolated AAMP reader (always). It needs `Syroot.*` 5.x where anything reading
  BFRES needs 2.x, so it lives in its own load context.
- `wrs-prepare\` - the out-of-process preparer. Set `<WrsOutOfProcessPreparer>false</WrsOutOfProcessPreparer>`
  if you prepare in-process and do not need it.

### GL state

The pipeline needs a GL 4.5 context (the decompiled shaders are `#version 450`). It was written
against GL's default global state; `GLHostState.Enter(gl)` puts the context into that state and
restores the host's afterwards. That matters for a host that uses reversed depth through
`glClipControl(LOWER_LEFT, ZERO_TO_ONE)` - every projection here is `[-1, 1]` and would lose its
near half - and for one that relies on its own buffer bindings or pixel-store settings persisting.
`WildRenderer` does this around everything it does; use it yourself around direct `SceneView` or
`DeferredPipeline` calls if your tool changes GL's defaults.

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
| `MESHCODEC_CLI` | A different `meshcodec_cli.exe`. |

## History

This renderer was developed inside the **Marrow** viewer and split out so other tools could use it.
`CLAUDE.md` carries the working knowledge from that development - read it before changing how
anything is drawn.
