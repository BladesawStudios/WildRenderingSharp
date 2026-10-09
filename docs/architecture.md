# Architecture

The renderer is split into a game-neutral core and one profile per game. A profile knows how its
game's shaders want to be fed; the core knows how to draw a frame.

Dependencies point one way. A namespace may use the ones with a lower layer number in the table, never a higher one, and
`LayeringTests` fails if one does. `PublicApiTests` pins the types a host can see; everything else is internal.

| Layer | Namespaces | What it is |
| --- | --- | --- |
| 0 | `Storage`, `Rom`, `Imaging`, `Logging` | the cache layout, ROM access, image export and the log; nothing here knows about rendering |
| 1 | `Gpu` | GL helpers: program building, uniform and texture helpers, the host's GL state, GPU matrix layout |
| 2 | `Rendering`, `Assets.Manifests` | camera and lighting state; the plain-data manifests of a prepared model |
| 3 | `Animation` | clips, curve evaluation and skeleton posing |
| 4 | `Graphics` | game-neutral contracts and data: what a profile consumes and produces, and the uniform block writer |
| 5 | `Shaders` | the program cache and the cleanup every decompiled shader needs |
| 6 | `Assets` | prepared-model loading: shapes, textures, materials, baked lighting |
| 7 | `Pipeline` | the pipeline, its targets, resources, drawing and passes; `Debug` |
| 8 | `Profiles` | everything specific to one game |
| 9 | `Scene` | `RenderActor` and the posing of a model for an actor |
| 10 | `Hosting` | views, background loading, the model preparers, content loading for a host |
| 11 | the root namespace | `WildRenderingSharp.WildRenderer` |

`Glsl/` holds the renderer's own shaders, one file each, embedded; `Pipeline/` is shared and `Totk/` is TotK's.

## The contract: `Graphics/`

The renderer describes a frame in neutral terms and a profile turns that into the bytes its
shaders read.

| Neutral input | Meaning |
| --- | --- |
| `CameraData` | view, projection, view-projection, inverse view, near/far, texel size |
| `SceneLightingData` | sun direction and colour, hemisphere ambient, volume mask, shadow map size |
| `SkinningData` | an actor's placement, skeleton and posed bones |

`IGameProfile` turns them into `Ubo`s (the bytes of one block, the `UboSpec` that names its shader block, binding and size, and the key
of the GL buffer it is kept in) and also supplies:

- `ShaderBindings` - the binding points the shared passes use for the camera, environment and material blocks
- `IShaderSources` - how a game's decompiled GLSL is cleaned, corrected and instanced
- `CreateFrameGraph` - the ordered stages of its frame

The renderer's world is right-handed and Y-up, the Wild games' own. Cameras, actor placements and the sun are given in it as they are, and the
uniform blocks are written without any conversion.

## A frame

`DeferredPipeline.RenderFrame` builds a `FrameContext` from the `FrameRequest` and hands it to the
profile's `IFrameGraph`, which runs its `IFrameStage`s in order. Each stage reads what earlier stages
left on the context and records what it produces.

TotK's order (`Profiles/Totk/TotkFrameGraph.cs`): setup, frame constants, G-buffer, shadows,
screen-space lighting, pass-ID mask, sky, resolve, grid, known material fixes, forward, exposure
measurement, lens flare, tonemap, colour correction, highlight.

Stages that use nothing game-specific (`FrameSetupStage`, `ScreenSpaceLightingStage`, `GridStage`,
`ForwardStage`, `ExposureMeasureStage`, `ColorCorrectionStage`, `HighlightStage`) live in
`Pipeline/Frame` and can be reused as they are.

## Adding a game

1. Create `Profiles/<Game>/` with a `<Game>Bindings` class naming each binding point.
2. Describe each uniform block as a `UboSpec` beside the slot constants of its layout, and fill it with a `UboWriter`. The camera, light and
   scene-material slots the Wild games share are in `Graphics/Ubos/Gsys*`, so write those through them. Build blocks from the neutral inputs, not from
   renderer internals; what each slot holds is in `uniform_blocks.md`.
3. Write the three small builders (camera, lighting, actor) and a `<Game>Profile : IGameProfile`
   that exposes them, its `ShaderBindings` and its `IShaderSources` (start from `DecompiledGlsl.Clean`).
4. Write the stages the game's frame needs. Reuse the neutral ones; put game-specific passes under
   `Profiles/<Game>/` next to the shaders they drive.
5. Assemble them in a `<Game>FrameGraph : IFrameGraph` and return it from `CreateFrameGraph`.
6. Pass the profile to `DeferredPipeline`.
7. Add snapshot tests for each new UBO builder (below).

The renderer carries a game's palette, sky, cloud and grade data as an opaque `IFrameEnvironment`
(`FrameRequest.Environment`). TotK's is `TotkEnvironment` in `Profiles/Totk/Atmosphere`, along with
`EnvPalette`, `SkyPostFx` and the rest of its data models; a profile's stages read the concrete type.

`LightingContext` holds only what every game reads: exposure, sun, scales, background mode. A game's own
live settings sit beside it (`WildRenderer.Totk` is a `TotkSettings`) and travel on its environment.

The prepared-model manifest carries what every game needs. Keys the loader does not know are kept in
`ShapeManifestEntry.Extensions`, surface as `LoadedShape.Tags`, and the profile reads them through its own
accessors (`LoadedShape.DeferredPass()` for TotK).

AAMP files are read with the AampSharp submodule (`vendor/AampSharp`), which any code can reference; a
profile decides which fields it takes from them (`PostFxAamp` for TotK).

## Shaders

The renderer's own GLSL lives as files under `src/WildRenderingSharp/Glsl/` and is read with
`GlslFiles.Load("Totk/Sky/LensFlare/Bright.frag")`. The game's decompiled shaders are never committed: the
preparer writes them into the cache and the pipeline loads them from there. `GlslFilesTests` fails the build if
GLSL reappears inside C# or a decompiler-named symbol turns up in `Glsl/`.

## Tests

```bash
dotnet test tests/WildRenderingSharp.Tests
```

`UboSnapshotTests` pins the exact bytes of every UBO builder to a recorded SHA-256 in `tests/WildRenderingSharp.Tests/Snapshots`; re-record after a
deliberate layout change with `WRS_UPDATE_SNAPSHOTS=1`. `LayeringTests` and `PublicApiTests` pin the layers and the public types. None of them needs a GL context.

`tests/WildRenderingSharp.TestBench` is the GL check: it prepares one actor from a romfs, renders it through the real pipeline in a hidden GL 4.5 window
and writes a PNG, failing on GL errors or a blank image. `--frames <n>` reports per-frame CPU and GPU time and allocations, and `--instances <n>` draws
that many copies as one batch.

```bash
dotnet run --project tests/WildRenderingSharp.TestBench -- --game totk --romfs <romfs dir> --actor Npc_Zelda_AncientHyrule --out zelda.png
```

`tests/WildRenderingSharp.RenderRegression` runs the test bench over a fixed set of scenes and compares each render with a baseline recorded on the same
machine; see its README.

What is known about the games' renderers is in [game-research.md](game-research.md).
