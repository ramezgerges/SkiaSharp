# SkiaSharp.Benchmarks.Rendering

Ganesh-vs-Graphite rendering benchmarks. Each configuration renders the same
`ISkiaScene` into a fixed-size offscreen `SKSurface` and blocks on GPU work
before recording the frame time — so the reported number is draw + record +
submit, not pixel readback.

## Layout

```
Backends/                       ← one IRenderBackend per (backend, GPU api) combo
  IRenderBackend.cs               Setup / RenderFrame / Flush contract
  BackendCatalog.cs               reflection discovery
  RasterBackend.cs                CPU baseline
  GaneshVulkanBackend.cs          Ganesh over Vulkan (Silk.NET)
  GraphiteVulkanBackend.cs        Graphite over Vulkan (Silk.NET)
  … one file per new backend
Benchmarks/
  SceneBenchmark.cs               [Params] over backend × scene
Program.cs                        BenchmarkSwitcher entry
```

Scenes come from two places, both merged by the reflection catalog:

- `tests/Tests/SkiaSharp/Visual/Scenes/*.cs` — the small, curated set the
  visual-regression matrix also uses (`DiagonalLines`, `FilledCircle`,
  `GradientBlend`, `RedRoundedRectOnWhite`, `Text`).
- `Scenes/*.cs` here — scenes ported from Flutter's
  `dev/benchmarks/macrobenchmarks/`, kept out of the visual matrix so they
  don't demand golden files. Every non-widget benchmark from Flutter's
  suite has a Skia equivalent here; the mapping is:

  | Flutter source | Scene here | Notes |
  |---|---|---|
  | `animated_advanced_blend` | `AdvancedBlend` | Non-`SrcOver` blend modes stacked |
  | `animated_blur_backdrop_filter`, `backdrop_filter`, `post_backdrop_filter` | `BackdropBlur` | `SaveLayerRec` with backdrop blur |
  | `animated_complex_image_filtered`, `filtered_child_animation` | `ImageFilterChain` | ColorFilter → Blur → DropShadow |
  | `animated_complex_opacity`, `cull_opacity`, `opacity_peephole` | `OpacityLayers` | 8 nested `SaveLayer(alpha)` |
  | `color_filter_and_fade` | `ColorFilterFade` | Color-matrix fade filter |
  | `color_filter_cache` | `ColorFilterMatrix` | Shared filter reused across draws |
  | `cubic_bezier`, `path_tessellation` | `CubicBezier` | 200 stroked cubic bezier paths |
  | `draw_arcs` | `DrawArcs` | 48 arcs with mixed `useCenter` |
  | `draw_atlas` | `DrawAtlas` | 200 sprites from a small atlas |
  | `draw_points` | `DrawPoints` | 900 points across 3 `SKPointMode`s |
  | `draw_vertices` | `DrawVertices` | 17×17 gouraud triangle mesh |
  | `gradient_perf` | `RadialSweepGradient` | Radial + sweep gradient shaders |
  | `large_images` | `LargeImage` | 512×512 image drawn with mipmap sampling |
  | `picture_cache` | `PictureCache` | Record once, replay 24× per frame |
  | `rrect_blur` | `RRectBlur` | Rrects with `SKMaskFilter` shadow blur |
  | `rsuperellipse_blur` | `SuperellipseBlur` | Discretized squircle (Skia has no rsuperellipse primitive) |
  | `shader_mask_cache` | `ShaderMask` | Gradient shader as DstIn mask |

Drop a new `ISkiaScene` in either directory and it appears in every backend's
column here automatically.

Vulkan bring-up (`SilkVkContext`) is also linked in from
`tests/VulkanTests/VkContexts/`. Backends that need Metal / GL / Dawn will
add their own analogue.

## Run

```
dotnet run -c Release --project benchmarks/SkiaSharp.Benchmarks.Rendering
```

BenchmarkDotNet args come after `--`. Useful ones:

```
# enumerate every (backend, scene) configuration
dotnet run … -- --list flat

# run one backend across all scenes
dotnet run … -- --filter '*Backend=raster*'

# run one scene across all backends (the Ganesh-vs-Graphite comparison view)
dotnet run … -- --filter '*Scene=DiagonalLines*'

# write results to a specific directory
dotnet run … -- --artifacts BENCH_RESULTS/2026-07-25
```

## Adding a backend

1. Add a public non-abstract `IRenderBackend` under `Backends/` with a
   parameterless constructor.
2. Fill in `Name`, `IsAvailable`, `UnavailableReason` (cheap probe, no
   context creation), and `Setup` / `RenderFrame` / `Flush` / `Dispose`.
3. That's it. Reflection discovery picks it up; every scene gets a row.

For a fresh backend, the fastest way is to copy `GraphiteVulkanBackend`
and change the SKGraphite* / GR* calls to whatever the target GPU api needs.

## Interpreting results

BenchmarkDotNet reports `Mean`, `Error`, `StdDev`, and (with `[MemoryDiagnoser]`)
allocated bytes. What to look at:

- **Ganesh vs Graphite on the same scene** — the intended comparison. A
  scene that shows Graphite as 30% faster than Ganesh is a real win; a
  scene where it's 30% slower is worth investigating.
- **Both GPU backends vs raster** — if raster wins on a small scene, the
  GPU submission overhead dominates. Not a bug, just useful context for
  scenes that are too tiny to benefit from GPU.
- **Warmup vs Workload** — a big gap means the first draw paid a lot
  (pipeline compile, texture upload, etc.). Consistent iteration cost after
  warmup is the interesting signal.

## Captured SKPictures (real-UI workloads)

Beyond the hand-written scenes, this project can replay serialized
`SKPicture` files captured from a real UI framework. Drop any `.skp` file
under `Captures/` and it becomes a benchmark row named
`Captured.<filename>`. Since a picture is a backend-neutral command
stream, the *same* real-UI workload replays identically on raster, Ganesh,
and Graphite — arguably the fairest possible comparison.

See [`Captures/HOW_TO_CAPTURE.md`](Captures/HOW_TO_CAPTURE.md) for the
Uno-side hook (small opt-in patch to `SkiaRenderHelper.skia.cs` gated by
`UNO_DUMP_SKPICTURE_DIR`).

To sanity-check the playback path without doing a real UI capture, use the
built-in recorder — it runs any hand-written scene through
`SKPictureRecorder` and drops the `.skp` next to the others:

```
dotnet run -c Release --project benchmarks/SkiaSharp.Benchmarks.Rendering -- \
    --record-scene GradientBlend --output Captures/Sample.GradientBlend.skp
```

## Follow-ons

- Backends: Ganesh-Metal, Ganesh-GL, Graphite-Metal, Graphite-Dawn (WASM).
- Real-UI captures from Uno.Gallery + Avalonia demos.
- Machine-readable output (JSON) for CI trend tracking / PR-comment diff tables.
