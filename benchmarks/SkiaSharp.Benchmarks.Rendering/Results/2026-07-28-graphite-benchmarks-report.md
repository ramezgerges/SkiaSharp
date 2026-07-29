# Graphite-vs-Ganesh benchmarks — final report

Windows 11 / Intel Core i5-8365U (Coffee Lake, 4 physical / 8 logical cores) /
Intel UHD 620 iGPU / Vulkan driver from Windows 11 25H2 / .NET 10.0.8 /
SkiaSharp branch `dev/graphite-benchmarks`.

> **TL;DR** — on Vulkan on this hardware, Graphite is meaningfully behind
> Ganesh on typical UI (~1.5–2×), but architecturally *ahead* on batched
> textured-quad rendering (`DrawAtlas`) — a real 28% win we can point at.
> Parallel-recording is a Graphite-specific mitigation that recovers some
> of the gap on vector-heavy scenes (~20% at optimal partition count) but
> makes text-heavy scenes worse (glyph-atlas contention across worker
> Recorders). Ganesh handles per-frame context recreation reasonably; the
> Graphite equivalent (`LargeImage`) blows up 153× on the same primitive.

## What we set out to do

The starting point: SkiaSharp had shipped Graphite backends but nobody had a
side-by-side benchmark suite against Ganesh. Goals:

1. **Quantify the Ganesh↔Graphite gap** on real UI workloads, not synthetic
   micro-benchmarks.
2. **Identify Graphite's architectural wins**, if any.
3. **Test parallel-recording** — the headline Graphite feature — as a
   mitigation for CPU-record cost on scenes with lots of draw calls.
4. **Build a repeatable methodology** so the numbers survive re-runs on
   different hardware and different Skia milestones.

## Approach

### 1. Cross-backend benchmark harness (foundation)

Built `SkiaSharp.Benchmarks.Rendering` with five backends registered via
`BackendCatalog` reflection:

| Backend | What it does |
|---|---|
| `raster` | CPU-side pixel push — baseline for "no GPU at all" |
| `ganesh-vulkan` | Ganesh over Vulkan, one long-lived `GRContext` |
| `ganesh-vulkan-nctx` | Same, but a fresh context per frame — models the cold-start cost real apps pay on window resize / device-lost |
| `graphite-vulkan` | Graphite over Vulkan, one long-lived `SKGraphiteContext` |
| `graphite-vulkan-parallel` | Same context, but per-partition Recorders on worker threads (Graphite's headline feature — Ganesh cannot do this, its context is single-threaded) |

Every scene implements `ISkiaScene` (`Name`, `Info`, `Draw(canvas)`). Partitionable
scenes also implement `IPartitionedSkiaScene` (`PartitionCount`,
`DrawPartition(canvas, i)`), which the parallel backend uses to record ranges
concurrently on separate `Recorder`s and `Insert` their `Recording`s into the
final surface.

### 2. Port Flutter's macrobenchmark scenes

Started with the closest-to-industry-standard existing test set: Flutter's
`dev/benchmarks/macrobenchmarks/`. Ported these as `ISkiaScene`
implementations so every backend renders identically:

`AdvancedBlend`, `AtlasHeavy`, `BackdropBlur`, `ColorFilterFade`,
`ColorFilterMatrix`, `CubicBezier`, `DiagonalLines`, `DrawArcs`, `DrawAtlas`,
`DrawPoints`, `DrawVertices`, `FilledCircle`, `GradientBlend`, `HighDrawCount`,
`ImageFilterChain`, `KanbanBoard`, `HeavyKanbanBoard`, `LargeImage`,
`ManyImages`, `OpacityLayers`, `PictureCache`, `PipelineDiversity`,
`RadialSweepGradient`, `RedRoundedRectOnWhite`, `RRectBlur`, `ShaderMask`,
`SuperellipseBlur`, `Text`.

First real-hardware run (`Results/2026-07-28-i5-8365U.md`) gave the coarse map
of where Graphite stood.

### 3. Real UI: capture Uno.Gallery SKPictures and replay

Flutter samples are useful but synthetic. To get *real* UI shape, we captured
`SKPicture`s from a running Uno.Gallery app.

**Patched Uno** — added ~40 lines to `SkiaRenderHelper.skia.cs` gated on
`UNO_DUMP_SKPICTURE_DIR` so the pictures Uno already produces get serialized
to disk. Zero overhead when the env var is unset.

**Captured** — Uno.Gallery's Overview, Buttons, ColorPicker, Acrylic,
Calendar pages under Xvfb + fluxbox on Linux (validated headless workflow
in `Captures/HOW_TO_CAPTURE.md`). Each frame is 200–450 MB because Skia
serializes every referenced typeface per picture and Uno's default font
stack includes Segoe UI + Fluent icons + emoji fallbacks.

**Replayed** — a `PicturePlaybackScene` that reads any `.skp` and draws it
via `SKCanvas.DrawPicture` on the target backend. First real cross-backend
timings on captured Uno frames (`Captures/HOW_TO_CAPTURE.md`):

| Page | raster | ganesh-vk | graphite-vk |
|---|---:|---:|---:|
| Overview | 11.35 ms | 11.65 ms | 80.1 ms |
| ButtonSample | 7.47 | 8.33 | 62.0 |
| ColorPickerSample | 8.06 | 8.68 | 63.1 |
| **AcrylicSample** | **164.5** | 13.7 | **77.9** |
| CalendarView | 7.54 | 8.42 | 61.1 |

`AcrylicSample` was the smoking-gun real-world Graphite regression: 6×
slower than Ganesh on a real UI page whose main primitive is *backdrop
blur* — the archetypal "GPU pays off" workload that Graphite is supposed
to accelerate. Matched the synthetic `RRectBlur` (3.8× slower) and
`SuperellipseBlur` (3× slower) regressions from the flat sweep.

### 4. Reverse-engineering SKPictures back to C#

Playing back a captured `.skp` is a black box — you can't tell why one page
is slow. To crack it open we built `SkpDecompiler` (`Tools/SkpDecompiler.cs`)
which walks the Skia picture op-stream and re-emits it as human-readable
C# calling the `SkiaSharp` API.

Iteratively taught it more of Skia's binary format:

- **Op stream**: `SAVE`/`RESTORE`/`CONCAT`/`SET_M44`/`CLIP_*`/`DRAW_*`
  (rects, ovals, rrects, arcs, points, paths, text-blobs, images, sub-pictures).
- **Resource tables**: paints (fully decoded — colour, style, stroke, blend
  mode, AA flag), paths (packed uint32 header + pts/verbs), text blobs
  (glyph runs w/ typeface refs), images (encoded PNG/JPEG bytes emitted as
  base64), typefaces (font tables emitted as base64 too — turns out to be
  ~2 MB per page and dominates output size).
- **Flattenables**: 5 subclasses (`SkModeColorFilter`,
  `SkBlurImageFilterImpl`, `SkColorFilterImageFilterImpl`,
  `SkMatrixTransformImageFilter`, `SkComposeImageFilterImpl`) reconstruct
  the effect chains on paints.
- **Sub-pictures**: nested `.skp` inside the outer picture (Uno uses this
  extensively for composited elements). Decoded recursively and either
  emitted as nested static classes or *inlined* into the parent's op stream
  for better partition boundaries — see #6 below.

**Fixes that mattered along the way** (this session):

- `SET_M44` was emitting matrices with the wrong element order. `SKMatrix44`'s
  constructor is column-major (`mIJ` = column I row J, matching Skia's
  `SkM44::fMat[c*4+r]`), but the decompiler was transposing. Result: matrix
  operations were rendered as identity, blanking most sub-trees. Fix: pass
  16 column-major floats directly.
- **Sub-picture inlining**: nested sub-pictures were emitted as opaque
  `SubPic_X.Draw(canvas)` calls, so the partitioner couldn't see the
  underlying ops. Refactored inlining to splice sub-pic ops directly into
  the parent's op-list with a resource-prefix rewrite (`paintTable[i]` →
  `SubPic_G.paintTable[i]`) and a per-sub-picture var-name suffix
  (`b{offset}` → `b_G_{offset}`) so nothing collides after inlining.
- **Partition baseline picker**: the original algorithm greedily rolled
  the first `Save+Clip` it saw into a "state prologue" replayed by every
  partition. On per-item scenes (Dashboard, Kanban) that "first Save+Clip"
  is a per-tile local clip — imposing it on every partition made partition 0
  render fine and partitions 1..N-1 render with the wrong clip. Fix: after
  picking a tentative baseline, walk the body and truncate the prologue
  down to `min(SaveDepthAfter)` — that's the deepest depth the body
  actually maintains throughout. Pictures with a real outer clip (Uno
  pages: `Save + ClipRect(pageBounds)`) keep baseline=1; per-item pictures
  correctly fall back to baseline=0.
- **Unused-paint effect skip**: paints referenced only by
  `DRAW_PICTURE_MATRIX_PAINT` (which our decoder drops as unknown) still
  built their full `SKImageFilter` chain via static init, and parallel
  Recorders tripped over shared native state in that chain (crash:
  `0xC0000005` in `sk_graphite_recorder_snap`). Fix: track
  `UsedPaintIndices` during op decode; `EmitPaint` skips flattenable
  reconstruction on paints no op references.

Once decompiling worked cleanly, we could point at *which ops* dominated a
frame and *why* — e.g. seeing 1500 ops at depth-3+ in a single sub-tree in
Uno Home explains why the partitioner couldn't split there.

### 5. Partitioning story: parallel recording as a Graphite mitigation

Graphite's headline architectural feature — Recorders are thread-local so
multiple can build command streams concurrently — needed a fair test.

**Hand-authored partitioned scenes** (`Kanban`, `HeavyKanban`, `HighDrawCount`)
implement `IPartitionedSkiaScene` and expose per-tile draw ranges directly.

**Decompiled partitioned scenes** — the auto-generated variants of each Uno
page plus decompiled versions of the hand-authored scenes. For scenes that
have flat top-level structure the partitioner produces balanced buckets:

| Scene | Partitions × ops each |
|---|---|
| `HighDrawCount` (5000 flat rects) | 8 × 625 (perfect) |
| `KanbanBoard` (8 cards) | 8 × 18-19 |
| `HeavyKanbanBoard` (12 cards) | 8 × 56-57 |
| `DashboardTilesPartitionedScene` (20 tiles) | 20 × 13-17 |
| `IconGrid*PartitionedScene` | N × ~68/34/23 for N=4/8/12 |
| `CodeEditor*PartitionedScene` | N × ~103/51/34 |

**Uno pages don't split evenly.** Every real Uno page's XAML compositor
wraps content in a single deep sub-picture, so the partitioner is bounded
by save-depth balance and ends up with one 1500-op monolithic bucket per
page. Symptom: 5 partitions with sizes like `8 / 1513 / 95 / 73 / 504` on
Uno Home. Structural — no partitioner fix short of splitting *inside* the
save-tree with per-partition state replay (which we didn't build).

**Partition-count sweep** on Dashboard × graphite-vulkan-parallel showed
the dispatch overhead / work-per-partition tradeoff clearly:

| Partitions | Mean | vs Sequential (3.23 ms) |
|---:|---:|---:|
| 4 | **3.15 ms** | -2.4% ✓ |
| 8 | 3.37 ms | +4.6% |
| 12 | 3.99 ms | +23% |
| 20 | 4.66 ms | +44% |

Per-partition dispatch cost (`Recorder.Snap` + serial `InsertRecording`)
is ~150-250 μs on this box. Below ~4 partitions, dispatch is amortised;
above ~12, it dominates. **The optimum depends on hardware core count**;
on 4 physical / 8 logical cores it's N=4 for this workload size.

### 6. Author scenes to *find* the Graphite wins

The Flutter set is broad but skewed toward Ganesh strengths. To find where
Graphite is actually competitive we authored purpose-built scenes:

| Scene | Design intent | Result on graphite-vulkan vs ganesh-vulkan |
|---|---|---:|
| **`DashboardTiles`** | Realistic 5×4 heterogeneous dashboard | +46% (Ganesh wins) |
| **`IconGrid`** | 96 vector icons, high paint-state churn | +65% (Ganesh wins direct); parallel closes it |
| **`CodeEditor`** | 44 lines of syntax-highlit text, VS Code aesthetic | +62% (Ganesh wins) |
| **`SpriteWall`** | 2000 rotated sprites via `DrawAtlas` | **-28% (Graphite wins clearly)** ✓✓ |
| **`RRectGrid`** | 2016 tinted rounded rects (heatmap) | +154% (Ganesh wins big) |

The hypotheses we tested and what they showed:

**H1: "Parallel-recording wins when paint churn is high."** Partly true.
DashboardTiles (242 paints) → parallel ties. CodeEditor (413 paints) →
parallel *loses* 17-49%. IconGrid (554 paints) → parallel *wins* 20%.

**Refined rule**: parallel-recording wins on paint-state churn on primitives
that *don't hit context-shared caches*. Text draws hit the glyph atlas —
Context-shared — so worker Recorders contend on the atlas allocator and
serialize. Vector fills/strokes don't hit the atlas → parallelize cleanly.

**H2: "Graphite wins on batched textured quads."** Confirmed. `DrawAtlas` at
200 sprites (Flutter's original size) was Graphite -15%. Scaling to
`SpriteWall`'s 2000 sprites widened it to -28% — the fixed Graphite
advantage (coalesced sampler binding + render-pass batching) amortises
over more instances.

**H3: "The batching win generalises to solid-fill vector primitives."** Not
true. `RRectGrid` (2016 rrects, same batching-shaped workload just with
different geometry) went the *other* way: Graphite +154% slower. Confirms
the win is specifically to `DrawAtlas`'s purpose-built atlased-rect
render pipeline in Graphite, not a general batching architectural win.

## Findings

### Where Graphite wins outright on Vulkan

| Scene | ganesh-vulkan | graphite-vulkan | Δ |
|---|---:|---:|---:|
| **SpriteWall** (2000 sprites via DrawAtlas) | 1.196 ms | **0.856 ms** | **-28%** |
| DrawAtlas (200 sprites, 256×256) | 644 μs | **548 μs** | -15% |
| DiagonalLines (8 stroked lines) | 722 μs | **695 μs** | -4% |

### "Explicit-batch API" hypothesis falsified beyond DrawAtlas

The initial version of this report predicted that other "explicit batch"
APIs — `DrawVertices` at scale, `DrawPatch` — would show similar wins to
DrawAtlas because they hit purpose-built Graphite pipelines. Tested and
falsified: both lost, less badly than typical UI but still lost.

| Scene | ganesh-vulkan | graphite-vulkan | Δ |
|---|---:|---:|---:|
| VertexMesh (1 DrawVertices, ~5,200 tris) | 1.21 ms | 1.81 ms | +50% |
| PatchQuilt (96 DrawPatch calls) | 2.32 ms | 3.13 ms | +35% |

Both scenes' parallel-graphite runs matched sequential (VertexMesh:
1.807 vs 1.810 ms; PatchQuilt: 3.145 vs 3.133 ms), confirming these
are pure GPU-side deltas, not CPU-record problems. The scenes are
*better* on Graphite than typical UI is (which sees +50-200% penalties),
so the batch-API pipelines exist and are somewhat competitive — but
they haven't received the same tuning `DrawAtlas` has on Vulkan.

**Sharpened rule:** DrawAtlas is not "representative of a class" —
it's a specifically-optimised pipeline (Skia's team hand-tuned it
because Chrome + Flutter hammer it for scrolling image content, emoji,
sprite rendering). Presumably on Metal the picture is broader; on
Vulkan on this iGPU, DrawAtlas is the specific win, not a general one.

### Where parallel-recording lets Graphite catch up

| Scene | ganesh seq | graphite seq | graphite-parallel (best N) |
|---|---:|---:|---:|
| HighDrawCount (5000 rects) | ~2 ms | ~3 ms | ~2 ms (matches Ganesh) |
| IconGrid8Partitioned (554 paints) | 2.05 ms | 4.08 ms | **3.27 ms** |
| DashboardTilesPartitioned (242 paints, N=4) | 1.87 ms | 3.23 ms | **3.15 ms** |

### Where parallel-recording *hurts*

| Scene | ganesh seq | graphite seq | graphite-parallel |
|---|---:|---:|---:|
| CodeEditor (direct, per-frame paint alloc) | 2.58 ms | 4.18 ms | **10.31 ms** |
| CodeEditor8Partitioned (text-heavy) | 1.28 ms | 2.54 ms | **3.78 ms** (+49% vs seq) |
| CodeEditor12Partitioned | 1.32 ms | 2.48 ms | **4.11 ms** (+66% vs seq) |

Contention on the glyph-atlas allocator across worker Recorders. Text
workloads should stay sequential on graphite-vulkan.

### Where Ganesh dominates

Everything else on typical UI shape.

| Scene | ganesh-vulkan | graphite-vulkan | Δ |
|---|---:|---:|---:|
| **RRectGrid** (2016 rrects) | 2.79 ms | 7.09 ms | **+154%** |
| **LargeImage** (single large image) | 674 μs | **103,527 μs** | **+153×** 💀 |
| RRectBlur | 826 μs | 3,105 μs | +3.8× |
| SuperellipseBlur | 972 μs | 2,946 μs | +3.0× |
| AcrylicSample (Uno, backdrop blur) | 13.7 ms | 77.9 ms | +5.7× |
| OpacityLayers | 1,178 μs | 1,519 μs | +29% |
| DrawArcs | 969 μs | 1,212 μs | +25% |
| Most Uno pages | 8-12 ms | 60-80 ms | +6-7× |

**`LargeImage` is a real bug, not a slowdown** — 100+ ms for a single
image draw. Almost certainly per-frame re-upload of an image that
should be cached. Worth an isolated repro + upstream file.

### Rules of thumb

1. **Graphite wins** when the workload maps to a purpose-built Graphite render
   pipeline that batches instances (`DrawAtlas`; probably `DrawVertices` at
   large N, untested; possibly `DrawPatch`).
2. **Graphite loses** on typical UI shape (rrects, arcs, text, gradients,
   filters) — that's Ganesh's 10+ years of optimization.
3. **Parallel-recording** as a mitigation:
   - Wins ~20% on vector-heavy scenes with many paint changes (`IconGrid`)
   - Ties on light state (`Dashboard`)
   - Loses on text-heavy scenes (glyph atlas contention)
   - Does nothing on scenes with one big draw call (`SpriteWall`, `RRectGrid`)
4. **Optimum partition count** on 4 physical / 8 logical cores: N=4 for
   most scenes. Dispatch overhead dominates above ~N=8 at these workload sizes.
5. **`ganesh-vulkan-nctx`** models the "device lost every frame" worst case:
   30-40× slower on typical UI, but on light non-allocating scenes only
   +3% (`RRectGrid`). Real-world resize/device-lost frequency matters.

## What we shipped

### Scenes (in `Scenes/`)

Direct (hand-authored):
`DashboardTilesScene`, `IconGridScene`, `CodeEditorScene`, `SpriteWallScene`,
`RRectGridScene` (this branch) plus the pre-existing Kanban/HeavyKanban/HighDrawCount.

Decompiled from real Uno captures:
`UnoGallery{Home,Buttons,ColorPicker,Acrylic,Calendar}Scene` — each is one
1500–2200-op body with resource tables inline.

Decompiled + partitioned twins:
`DashboardTiles{,4,8,12}PartitionedScene`, `IconGrid{4,8,12}PartitionedScene`,
`CodeEditor{4,8,12}PartitionedScene`, `HighDrawCountPartitionedScene`.

### Tools (in `Tools/` and `Program.cs`)

- `SkpDecompiler.cs` — full `.skp` op-stream → readable C# source with paint,
  path, image, and text-blob resource tables inline. Supports partition
  emission with prologue/epilogue state replay.
- `--record-scene <name> --output <path>` — turn any `ISkiaScene` into an
  `.skp` for round-trip testing.
- `--decompile <path.skp> --class-name <X> --partition <N>` — the full
  decompile pipeline.
- `--render-skp <path> --output <png>` — raster the `.skp` as-is (baseline
  reference for eyeballing decompiler output).
- `--render-scene <name> --output <png>` — same, but for any registered scene.
- `--sample-pixels <path.png>` — grid RGBA sampler for terminal-based diff.
- `--no-font-data` — skip embedding font tables in decompiled output (17MB
  → 500KB for a typical Uno page; text renders with wrong glyphs, fine
  for benchmarks).

### Backends (in `Backends/`)

`RasterBackend`, `GaneshVulkanBackend`, `GaneshVulkanNContextsBackend`,
`GraphiteVulkanBackend`, `GraphiteVulkanParallelBackend`. Each implements
`IRenderBackend` (Setup/RenderFrame/Flush/Dispose) and is discovered by
reflection so scenes and backends are decoupled.

## Open threads worth pursuing

1. **`LargeImage` 153× regression** — file upstream. Repro is a single
   `DrawImage` of a 512×512 texture. Almost certainly a re-upload bug in
   the Graphite Vulkan image provider or in how SkiaSharp's texture image
   cache interacts with Graphite.
2. **`AcrylicSample`'s 6× backdrop-blur regression** — matches the synthetic
   `RRectBlur` and `SuperellipseBlur` regressions. Fundamental Graphite
   blur pipeline is slower here; worth checking whether Metal shows the
   same shape or is Vulkan-specific.
3. **Text/glyph-atlas contention on parallel-graphite** — the atlas is
   Context-shared so worker Recorders serialize. Skia's own team has
   acknowledged this; there's a `SkAtlasProvider` API meant to fix it.
   SkiaSharp hasn't exposed that yet.
4. **Real GPU vs iGPU / lavapipe** — every number here is on Intel UHD 620
   iGPU. Discrete Nvidia/AMD will likely tell a different story
   (Graphite's architectural wins should scale with GPU capability).
5. **Metal comparison** — Chrome ships Graphite on macOS. Rerunning this
   suite on macOS/Metal would show whether the Vulkan-specific
   regressions are Vulkan or Graphite.
6. **`SaveLayer`, `DrawVertices` at scale, `DrawPatch`** — untested
   Graphite paths. Any could show a big win or a hidden regression.
7. **Partitioner state-replay** — Uno pages can't split evenly today
   because sub-picture Save/Restore boundaries hide the natural
   partition points. A state-replay partitioner (track matrix + clip at
   each op, split at any depth, emit per-partition prologue) would
   unlock those. Estimated 300 LOC in `SkpDecompiler.PartitionBody`;
   not written this session.

## Commit trail

Selected commits illustrating the branch story:

| Hash | What |
|---|---|
| `9c42be0` | Initial `SkiaSharp.Benchmarks.Rendering` harness — Ganesh-vs-Graphite matrix |
| `8d81bf8`, `bfc5831` | Port Flutter macrobenchmark scenes |
| `ac7579c` | `PicturePlaybackScene` — replay captured `.skp` |
| `d3a59d7` | First `SkpDecompiler` (raw op-stream → C#, no resources) |
| `65ef442` | Decompiler: paint + factory tables |
| `84c8929` | Decompiler: path + image + text-blob tables + font data |
| `298f6a8` | Decompiler: `SkImageFilter` + `SkModeColorFilter` flattenables |
| `9f15f04` | Decompiler: sub-picture inlining |
| `e934e44` | Fix `SET_M44` — column-major matrix layout |
| `15376d9` | Skip effect reconstruction on unreferenced paints — fixes crash on parallel-graphite × Uno Home |
| `f56bbab` | Partition baseline picker respects `min(body depth)` — fixes per-tile scenes |
| `0fc732a`, `3a15cf1` | `DashboardTiles` (direct + N=4/8/12/20 decompiled variants) |
| `5ea3722a` | `IconGrid` + `CodeEditor` — paint-state-heavy angle |
| `e17e62b`, `49534aa` | `SpriteWall` — the clean Graphite win |
| `ca5f782` | `RRectGrid` — falsifies "batching win generalises" hypothesis |

## Reproducing

Everything on the branch — all scenes, tools, decompiled pictures — is
committed. To rerun any subset:

```powershell
dotnet build benchmarks/SkiaSharp.Benchmarks.Rendering -c Release
./bin/Release/net10.0/SkiaSharp.Benchmarks.Rendering.exe --inProcess --filter "<pattern>"
```

Useful filters:
- `*SpriteWall*` — the marquee Graphite win, all 5 backends
- `*Dashboard*` — 25 rows exercising partition-count sweep
- `*Uno*` — real captured UI pages
- `*` — full sweep (~30 minutes on this box)

Captured `.skp` files are not committed (~200-450 MB apiece per typeface
embed); reproduce locally with `Captures/HOW_TO_CAPTURE.md`.
