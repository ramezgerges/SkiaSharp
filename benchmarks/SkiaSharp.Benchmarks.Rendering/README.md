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
- `Scenes/*.cs` here — heavier scenes ported from Flutter's
  `dev/benchmarks/macrobenchmarks/`, kept out of the visual matrix so they
  don't demand golden files: `BackdropBlur`, `CubicBezier`, `OpacityLayers`,
  `DrawAtlas`, `RRectBlur`, `DrawVertices`.

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

## Follow-ons

- Backends: Ganesh-Metal, Ganesh-GL, Graphite-Metal, Graphite-Dawn (WASM).
- More scenes: backdrop filters, layer compositing, path-heavy, Skottie playback.
- Machine-readable output (JSON) for CI trend tracking / PR-comment diff tables.
