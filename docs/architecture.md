# Architecture

The renderer is split into a game-neutral core and one profile per game. A profile knows how its
game's shaders want to be fed; the core knows how to draw a frame.

```
src/WildRenderingSharp/
  Graphics/        game-neutral contracts: what a profile consumes and produces
  Pipeline/        the pipeline, its render targets, and the passes that do not depend on a game
    Frame/         FrameContext, IFrameStage and the stages that are game-neutral
  Profiles/Totk/   everything specific to Tears of the Kingdom
  Profiles/Botw/   everything specific to Breath of the Wild
  Shaders/         what both games' decompiled GLSL needs: its cleanup, support buffer and bindings
  Glsl/            the renderer's own shaders, one file each, embedded; Pipeline/ is shared, Totk/ is TotK's
  Assets/          prepared-model loading: manifests, textures, shapes
  Rendering/       camera, lighting state, palettes, animation evaluation
  Scene/           RenderActor
  Hosting/         SceneView, GL host state, the model preparers
```

## The contract: `Graphics/`

The renderer describes a frame in neutral terms and a profile turns that into the bytes its
shaders read.

| Neutral input | Meaning |
| --- | --- |
| `CameraData` | view, projection, view-projection, inverse view, near/far, texel size |
| `SceneLightingData` | sun direction and colour, hemisphere ambient, volume mask, shadow map size |
| `SkinningData` | an actor's placement, skeleton and posed bones |

`IGameProfile` turns them into `UniformBlock`s (a key, a binding point and the bytes) and also
supplies:

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
2. Add one class per uniform block under `Profiles/<Game>/Ubos/`, implementing `IUboBlock`.
   Build them from the neutral inputs, not from renderer internals.
3. Write the three small builders (camera, lighting, actor) and a `<Game>Profile : IGameProfile`
   that exposes them, its `ShaderBindings` and its `IShaderSources` (start from `DecompiledGlsl.Clean`).
4. Write the stages the game's frame needs. Reuse the neutral ones; put game-specific passes under
   `Profiles/<Game>/` next to the shaders they drive.
5. Assemble them in a `<Game>FrameGraph : IFrameGraph` and return it from `CreateFrameGraph`.
6. Pass the profile to `DeferredPipeline` (its `profile` parameter defaults to TotK).
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

`UboSnapshotTests` pins the exact bytes of every UBO builder to a recorded SHA-256 in
`tests/WildRenderingSharp.Tests/Snapshots`. `TotkProfileTests` checks the profile produces what the
pipeline used to build inline. Neither needs a GL context.

After a deliberate layout change, re-record with `WRS_UPDATE_SNAPSHOTS=1`.

`tests/WildRenderingSharp.TestBench` is the GL check the unit tests cannot be: it prepares one actor from a
romfs, opens a hidden GL 4.5 window, renders it through the real pipeline (sky, clouds, deferred lighting) and
writes a PNG, failing on GL errors or a blank image:

```bash
dotnet run --project tests/WildRenderingSharp.TestBench -- --game totk --romfs <romfs dir> --actor Npc_Zelda_AncientHyrule --out zelda.png
```

`--game botw --romfs <Switch dump> --actor Link` prepares the model from the dump's packs and draws it through `Profiles/Botw`: the game's own G-buffer programs, then its own character shading passes (`--yaw <degrees>`, `--distance`, `--height` move the camera).

Reverse-engineering findings that used to sit in XML docs (Ghidra addresses, manifest format history) are in
[game-notes.md](game-notes.md).
