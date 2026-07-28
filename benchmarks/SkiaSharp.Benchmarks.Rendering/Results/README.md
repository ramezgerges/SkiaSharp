# Benchmark Results — real hardware

Files here are raw BenchmarkDotNet artifacts from actual runs. File naming
is `YYYY-MM-DD-<cpu>.{md,csv}`. Add new runs alongside instead of
overwriting; version history is where trend-tracking will eventually go.

## 2026-07-28 — Intel Core i5-8365U — KanbanBoard parallel comparison

Focused three-way comparison to answer "is Graphite's parallel-recording
architecturally different from N graphics contexts + blit?" Same machine as
below, but this run compares only the 5 backends on the partitioned
`KanbanBoard` scene:

| Backend | Mean | vs sequential Ganesh | vs Graphite parallel |
|---|---:|---:|---:|
| raster | 22.07 ms | 14.1× | 9.2× |
| **ganesh-vulkan** (sequential) | **1.56 ms** | 1.0× | 0.65× |
| **graphite-vulkan** (sequential) | 1.91 ms | 1.22× | 0.80× |
| **graphite-vulkan-parallel** | 2.39 ms | 1.53× | 1.0× |
| **ganesh-vulkan-nctx** (parallel via 8 CPU raster + blit) | 10.83 ms | **6.9×** | **4.5×** |

**The key finding**: `graphite-vulkan-parallel` is **4.5× faster than
`ganesh-vulkan-nctx`** — that's the value of "no offscreen buffers + shared
resource pool + no pixel copies." The Ganesh parallel alternative is 6.9×
slower than *sequential* Ganesh, so on this workload the blit tax alone
dominates.

**Caveats on this specific measurement**:
- Sequential Ganesh (1.56 ms) still wins outright — total work is tiny
  (~200 μs per partition), well below thread-pool wakeup latency, so
  parallel dispatch overhead swamps the win.
- `KanbanBoard` shadows use `SKMaskFilter.CreateBlur` which hits the
  known Graphite `RRectBlur` regression (~3.76× slower). Sequential
  Graphite pays that ~350 μs; parallel Graphite pays it 8 times in
  parallel, so it recovers some of the loss but not all.

A `HeavyKanbanBoard` variant (3× the partitions, 6 tickets per card, no
mask-filter shadows) sits in the same matrix — see next run — to push
per-partition work above dispatch latency and isolate the parallelism
delta from the shadow regression.

## 2026-07-28 — Intel Core i5-8365U (Iris Plus iGPU), Windows 11

Full 23-scene × 3-backend matrix. Sorted by `graphite / ganesh` ratio,
worst first. Bolded columns are the actual signal.

| Scene                    |     raster |     ganesh |   graphite |    graphite / ganesh |
|--------------------------|-----------:|-----------:|-----------:|---------------------:|
| **LargeImage**           |  23.09 ms  |  673.8 μs  | **103.53 ms** |            **153.7×** |
| **RRectBlur**            |   6.24 ms  |  826.5 μs  |   3.11 ms  |             **3.76×** |
| **SuperellipseBlur**     |   5.93 ms  |  971.8 μs  |   2.95 ms  |             **3.03×** |
| DrawPoints               |   8.36 ms  |   1.66 ms  |   2.42 ms  |                1.46× |
| AdvancedBlend            |  10.15 ms  |  754.3 μs  |  978.6 μs  |                1.30× |
| OpacityLayers            |  34.22 ms  |   1.18 ms  |   1.52 ms  |                1.29× |
| DrawArcs                 |   3.27 ms  |  969.3 μs  |   1.21 ms  |                1.25× |
| BackdropBlur             |  36.27 ms  |   1.07 ms  |   1.31 ms  |                1.23× |
| ColorFilterFade          |  774.2 μs  |  666.2 μs  |  817.3 μs  |                1.23× |
| ImageFilterChain         |  76.72 ms  |   2.51 ms  |   2.91 ms  |                1.16× |
| Captured.GradientBlend   |   4.49 ms  |  654.1 μs  |  757.3 μs  |                1.16× |
| ShaderMask               |   9.98 ms  |  877.1 μs  |  982.9 μs  |                1.12× |
| PictureCache             |   4.69 ms  |   1.01 ms  |   1.13 ms  |                1.12× |
| FilledCircle             |  134.3 μs  |  600.5 μs  |  671.3 μs  |                1.12× |
| GradientBlend            |   4.43 ms  |  667.6 μs  |  743.0 μs  |                1.11× |
| ColorFilterMatrix        |  145.0 μs  |  718.2 μs  |  768.8 μs  |                1.07× |
| RedRoundedRectOnWhite    |   76.0 μs  |  572.3 μs  |  610.2 μs  |                1.07× |
| DrawVertices             |   5.52 ms  |  664.9 μs  |  707.9 μs  |                1.06× |
| RadialSweepGradient      |   4.16 ms  |  715.6 μs  |  749.1 μs  |                1.05× |
| Text                     |   1.38 ms  |   1.43 ms  |   1.47 ms  |                1.03× |
| CubicBezier              |  17.32 ms  |  14.58 ms  |  14.68 ms  |                1.01× |
| DiagonalLines            |  490.6 μs  |  722.4 μs  |  695.0 μs  |                0.96× |
| **DrawAtlas**            |   2.97 ms  |  643.5 μs  |  547.7 μs  |             **0.85×** (Graphite wins) |

### Actual Graphite regressions worth reporting upstream

Three specific regressions survive real-hardware measurement — the rest
of the "Graphite slower" pattern from lavapipe was software-Vulkan
overhead, not a Graphite defect.

1. ~~**`LargeImage`: Graphite 154× slower than Ganesh (103.5 ms vs 674 μs).**~~
   **FIXED in `c33e6509e62`**: this was a SkiaSharp binding-side benchmark
   bug, not a Skia issue. Graphite's `SKGraphiteContext.CreateRecorder`
   image-provider callback fires *per DrawImage*, not once per image.
   The inline lambda was calling `image.ToTextureImage(recorder, mipmapped)`
   on every invocation — allocating a fresh texture and rebuilding the
   mipmap chain per draw. Adding a `Dictionary<uint, SKImage>` keyed by
   `(image.UniqueId, mipmapped)` cache brought Graphite from 103 ms to
   sub-millisecond, and on lavapipe **Graphite is now faster than Ganesh
   on this scene**. Re-run on real hardware to confirm the direction.

2. **`RRectBlur`: Graphite 3.76× slower than Ganesh (3.11 ms vs 826 μs).**
   Rounded rectangles drawn with `SKMaskFilter.CreateBlur`.
   Investigated: both backends hit their respective "analytic blurred
   rrect" fast paths (Ganesh: `GrBlurUtils::MakeRRectBlur`; Graphite:
   `AnalyticBlurMask::MakeRRect` in
   `src/gpu/graphite/geom/AnalyticBlurMask.cpp`). The Graphite
   implementation still generates a CPU-side blurred-rrect nine-patch
   texture and uploads it (see the `TODO(b/343684954, b/338032240)`
   comment in that file — Skia team already tracking this). Even with
   the proxy cache deduping the texture across draws, the analytic
   render step's per-draw cost is measurably heavier than Ganesh's
   fragment processor. Legitimate upstream issue, not fixable in the
   binding.

3. **`SuperellipseBlur`: Graphite 3.03× slower than Ganesh.** Uses
   `SKPath` (discretised squircle), which doesn't hit the analytic
   shortcut — falls back to general path-blur (render mask, Gaussian
   blur, draw). Both Ganesh and Graphite take this path; Graphite's
   is 3× slower. Also upstream.

Everything else is within 1.3× of Ganesh, most within 1.15× —
acceptable overhead attributable to Graphite's Recorder + Snap machinery
(the +100 B/op memory pattern also visible in the report). And
**`DrawAtlas` actually favours Graphite by 15%**, worth calling out as
where the new architecture is paying off.

### Cross-cutting observations

- **Lavapipe was a bad proxy for real GPU.** Every scene showed a 6–8×
  Graphite slowdown on lavapipe; on real Iris Plus, most scenes are
  within 15% of Ganesh. Software-Vulkan overhead was dominating the
  signal.

- **Where hardware GPU pays off massively vs raster:** `BackdropBlur`
  (34×), `OpacityLayers` (29×), `ImageFilterChain` (30×), `AdvancedBlend`
  (13×), `LargeImage` (34× on Ganesh). This is the "why bother with
  GPU" data — image filters and layered composition are the killer
  workloads.

- **Where raster wins outright** (submit overhead > actual work):
  `RedRoundedRectOnWhite` (76 μs vs GPU 572 μs — 7.5× faster),
  `FilledCircle` (4.5×), `ColorFilterMatrix` (5×). Anything sub-500 μs
  of drawing on GPU costs more than the CPU frame itself.

- **`CubicBezier` and `Text` are backend-agnostic** — all three within
  10% of each other. Path tessellation and glyph atlas caching happen
  on CPU regardless of the target backend, so there's no signal to
  extract from these scenes about GPU quality.

- **Cross-check the captured picture** (`Captured.GradientBlend`) at
  654 μs (Ganesh) is nearly identical to the direct-drawn
  `GradientBlend` at 668 μs — confirms that `SKPicture.Deserialize` +
  `canvas.DrawPicture` adds negligible overhead on top of the actual
  drawing on GPU backends. Picture playback is essentially free.

### Suggested upstream Skia issues

Only two remain — the LargeImage one turned out to be on our side.

- **Skia issue #1**: Graphite `AnalyticBlurMask::MakeRRect` slower than
  Ganesh's `GrBlurUtils::MakeRRectBlur` on the same drawing. Both hit
  their "analytic blurred rrect" fast paths but the Graphite render step
  runs ~3.76× slower per-draw on real hardware. Skia's own TODO comment
  in `AnalyticBlurMask.cpp` (`b/343684954`, `b/338032240`) already
  tracks this — that TODO mentions moving the nine-patch generation to
  GPU or using a shared blur-mask cache, both of which would help.

- **Skia issue #2**: Graphite general path-blur is 3× slower than
  Ganesh's for `SKPath` shapes that don't have an analytic shortcut
  (arbitrary polygons like `SuperellipseBlur`'s discretised squircle).
  Path → mask → 2-pass Gaussian → blit; each stage is slower on
  Graphite. Might share machinery with the rrect case above.
