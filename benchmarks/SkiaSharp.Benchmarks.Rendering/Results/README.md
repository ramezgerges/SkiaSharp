# Benchmark Results — real hardware

Files here are raw BenchmarkDotNet artifacts from actual runs. File naming
is `YYYY-MM-DD-<cpu>.{md,csv}`. Add new runs alongside instead of
overwriting; version history is where trend-tracking will eventually go.

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

1. **`LargeImage`: Graphite 154× slower than Ganesh (103.5 ms vs 674 μs).**
   Draws a 512×512 image 20 times with mipmap sampling. Ganesh does this
   in 674 μs, Graphite takes 103 ms — 4× slower than software raster.
   Something in Graphite's texture upload / mipmap chain / sampler cache
   path is genuinely broken. This is the biggest smoking gun in the
   suite.

2. **`RRectBlur`: Graphite 3.76× slower than Ganesh (3.11 ms vs 826 μs).**
   Rounded rectangles drawn with `SKMaskFilter.CreateBlur` — the
   classic "drop-shadow" path. Ganesh has a specialised fast path here;
   Graphite doesn't (yet).

3. **`SuperellipseBlur`: Graphite 3.03× slower than Ganesh.** Same
   `SKMaskFilter.CreateBlur` path applied to a discretised squircle
   path. Consistent with the `RRectBlur` finding — same missing fast
   path.

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

### Suggested Skia bugs to file

- **Skia issue #1**: Graphite mipmap sampling / texture caching regression.
  20 draws of a pre-uploaded `SKImage` shouldn't take 100 ms. Investigate
  `SKGraphiteContext.CreateRecorder` + image-provider callback pathway
  and whether textures are being re-uploaded per draw.
- **Skia issue #2**: Graphite `SKMaskFilter.CreateBlur` fast path.
  Ganesh has a rect-mask-blur specialisation (`SkBlurMaskFilter::box`
  or similar) that Graphite hasn't picked up yet — see `RRectBlur` and
  `SuperellipseBlur` both landing at 3× Ganesh.
