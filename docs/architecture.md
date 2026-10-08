# Architecture

The renderer is split into a game-neutral core and one profile per game. A profile knows how its
game's shaders want to be fed; the core knows how to draw a frame.

```
src/WildRenderingSharp/
  Graphics/        game-neutral contracts: what a profile consumes and produces
  Pipeline/        the pipeline, its render targets, and the passes that do not depend on a game
    Frame/         FrameContext, IFrameStage and the stages that are game-neutral
  Profiles/Totk/   everything specific to Tears of the Kingdom
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
- `IWorldBasis` - how the renderer's Z-up world maps to the game's (`YUpWorldBasis` for the Wild games)
- `IShaderSources` - how a game's decompiled GLSL is cleaned, corrected and instanced
- `CreateFrameGraph` - the ordered stages of its frame

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
   that exposes them, its `ShaderBindings`, its `IShaderSources` and its world basis.
4. Write the stages the game's frame needs. Reuse the neutral ones; put game-specific passes under
   `Profiles/<Game>/` next to the shaders they drive.
5. Assemble them in a `<Game>FrameGraph : IFrameGraph` and return it from `CreateFrameGraph`.
6. Pass the profile to `DeferredPipeline` (its `profile` parameter defaults to TotK).
7. Add snapshot tests for each new UBO builder (below).

The renderer carries a game's palette, sky, cloud and grade data as an opaque `IFrameEnvironment`
(`FrameRequest.Environment`). TotK's is `TotkEnvironment` in `Profiles/Totk/Atmosphere`, along with
`EnvPalette`, `SkyPostFx` and the rest of its data models; a profile's stages read the concrete type.

Still shared between games, and so still TotK-shaped: the prepared-model manifest format, and
`LightingContext`, which holds both neutral display state (exposure, sun, bloom) and TotK sky and
cloud settings. Where BotW differs, those are the next things to move behind the profile.

## Tests

```bash
dotnet test tests/WildRenderingSharp.Tests
```

`UboSnapshotTests` pins the exact bytes of every UBO builder to a recorded SHA-256 in
`tests/WildRenderingSharp.Tests/Snapshots`. `TotkProfileTests` checks the profile produces what the
pipeline used to build inline. Neither needs a GL context.

After a deliberate layout change, re-record with `WRS_UPDATE_SNAPSHOTS=1`.
