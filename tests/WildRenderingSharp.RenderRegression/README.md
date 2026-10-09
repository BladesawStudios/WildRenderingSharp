# Render regression

Renders a fixed set of scenes through the real pipeline and compares them with images recorded earlier, so a change to a pass,
a shader or a uniform block that alters the picture is caught. The scenes are in `scenes.json`: TotK sky, lighting and shadow
scenes, a grid of instanced actors, and two BotW actors.

It needs your own ROM dumps and a GPU, so it is run by hand. Baselines are per machine and driver, so they are kept in `golden/`
(ignored by git), not in the repository.

```
dotnet build
dotnet run --project tests/WildRenderingSharp.RenderRegression -- record --totk-romfs <dir> --botw-romfs <dir>   # once, on a good build
dotnet run --project tests/WildRenderingSharp.RenderRegression -- check  --totk-romfs <dir> --botw-romfs <dir>   # after a change
```

`WRS_TOTK_ROMFS`, `WRS_BOTW_ROMFS`, `WRS_TOTK_CACHE` and `WRS_BOTW_CACHE` stand in for the options, and a game with no romfs is
skipped. `--scenes a,b` runs some of them.

Each scene is rendered by the test bench in a process of its own, with the clouds held still so the same build renders the same
image every time. A scene passes when its mean per-channel difference is at most 0.05 out of 255 and at most 0.01% of its
pixels differ visibly (see `--mean` and `--over16`). A failing scene writes `out/<scene>.diff.png`, the difference at eight times
its size. When a change is meant to alter a scene, run `record` for it again (`--scenes`).
