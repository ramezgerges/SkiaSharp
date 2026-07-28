# Capturing SKPictures from real UIs

Every `.skp` file in this directory becomes a benchmark row automatically —
`CapturedSceneRegistry` discovers them and creates one `PicturePlaybackScene`
per file. The scene name shows up as `Captured.<filename-without-.skp>` in
BenchmarkDotNet's output.

The idea: a real UI framework already produces `SKPicture` command streams for
every frame it renders. If we intercept that picture and serialize it, we get a
backend-neutral capture that replays identically on raster, Ganesh, and
Graphite. That's the fairest possible workload — the same command stream,
executed by three different backends.

## Uno Platform

Uno already goes `compose → SKPicture → draw` internally. The hook adds an
opt-in serialization step gated by `UNO_DUMP_SKPICTURE_DIR`.

### 1. Patch Uno

In your Uno checkout, edit `src/Uno.UI/Helpers/SkiaRenderHelper.skia.cs`.

Add these `using`s at the top:

```csharp
using System.IO;
using System.Runtime.InteropServices;
using Uno.Foundation.Logging;
```

Add these members near the `_recorder` field:

```csharp
private static readonly string? _dumpDir = Environment.GetEnvironmentVariable("UNO_DUMP_SKPICTURE_DIR");
private static int _dumpCounter;

// libSkiaSharp exposes the raw C API; we need ref + serialize + data unpack
// to dump a picture without disturbing the caller's ownership of the handle.
[DllImport("libSkiaSharp")] private static extern void sk_picture_ref(IntPtr picture);
[DllImport("libSkiaSharp")] private static extern IntPtr sk_picture_serialize_to_data(IntPtr picture);
[DllImport("libSkiaSharp")] private static extern IntPtr sk_data_get_data(IntPtr data);
[DllImport("libSkiaSharp")] private static extern IntPtr sk_data_get_size(IntPtr data);
[DllImport("libSkiaSharp")] private static extern void sk_data_unref(IntPtr data);
```

At the end of `RecordPictureAndReturnPath`, just before `return`, add:

```csharp
if (_dumpDir is not null && picture != IntPtr.Zero)
{
    DumpPicture(picture, width, height);
}
```

Add the helper anywhere in the class:

```csharp
private static void DumpPicture(IntPtr picture, float width, float height)
{
    try
    {
        Directory.CreateDirectory(_dumpDir!);
        var data = sk_picture_serialize_to_data(picture);
        if (data == IntPtr.Zero) return;
        try
        {
            var size = (int)sk_data_get_size(data);
            var bytes = new byte[size];
            Marshal.Copy(sk_data_get_data(data), bytes, 0, size);
            var n = Interlocked.Increment(ref _dumpCounter);
            var name = $"frame-{n:D4}-{width:F0}x{height:F0}.skp";
            File.WriteAllBytes(Path.Combine(_dumpDir!, name), bytes);
        }
        finally { sk_data_unref(data); }
    }
    catch (Exception ex)
    {
        typeof(SkiaRenderHelper).Log().LogWarning($"UNO_DUMP_SKPICTURE_DIR dump failed: {ex}");
    }
}
```

Zero overhead when `UNO_DUMP_SKPICTURE_DIR` is unset — the top-level check
short-circuits before any P/Invoke.

### 2. Build a Skia-hosted Uno app that exercises the pages you care about

Any of:
- [Uno.Gallery](https://github.com/unoplatform/Uno.Gallery) — a broad
  showcase of Uno controls; good "real UI" coverage.
- The Uno checkout's `src/SamplesApp/SamplesApp.Skia.Generic` — Uno's own
  sample app, ships alongside the mainline repo.
- A minimal repro of the specific UI you want to benchmark.

Reference your patched Uno via `crosstargeting_override.props` (see Uno's
own docs) so the app picks up your local build.

### 3. Run with the env var set, click through the pages

```powershell
$env:UNO_DUMP_SKPICTURE_DIR = "C:\captures"
dotnet run --project path\to\Uno.Gallery.Skia
# navigate to the pages you want captured; each frame dumps
# frame-<N>-<W>x<H>.skp into C:\captures
```

Frames pile up fast (one per render tick). Two options:
- **Sampling**: hit each interesting page, wait for the UI to settle, note
  the counter — then use only the last N frames per page.
- **Renaming**: rename the dumps by hand as you go —
  `Gallery.HomePage.skp`, `Gallery.ListView.skp`, etc. The scene name shown
  in benchmark output is `Captured.<filename-without-.skp>`, so a
  distinctive name is worth the effort.

### 4. Drop the `.skp` files here

```
benchmarks/SkiaSharp.Benchmarks.Rendering/Captures/
    Gallery.HomePage.skp
    Gallery.ListView.skp
    ...
```

The csproj's `<Content Include="Captures\**\*.skp" />` copies them to the
build output; `CapturedSceneRegistry` picks them up at benchmark startup.

Rebuild + run:

```
dotnet run -c Release --project benchmarks/SkiaSharp.Benchmarks.Rendering -- \
    --filter "*RenderScene*Captured.Gallery*" --job medium
```

## Headless capture on Linux (validated 2026-07-28)

The full workflow works headlessly under Xvfb + fluxbox — no display needed.
Validated end-to-end on this branch: patched Uno's `SkiaRenderHelper`, ran
`SamplesApp.Skia.Generic` under `Xvfb :99`, captured 6 frames, replayed one on
raster at 15.3 ms.

```bash
# One-time setup
sudo apt-get install -y xvfb fluxbox xdotool x11-utils

# Launch
rm -f /tmp/.X99-lock
Xvfb :99 -screen 0 1024x768x24 -ac -nolisten tcp &
DISPLAY=:99 fluxbox &
DISPLAY=:99 UNO_DUMP_SKPICTURE_DIR=/tmp/captures \
    dotnet path/to/SamplesApp.Skia.Generic.dll
```

Splice in a matching `libSkiaSharp.so` if Uno's bundled NuGet native lib
doesn't match the SkiaSharp managed API version (the version guard will trip
otherwise — the message is unambiguous). Copy your build into
`bin/Release/net10.0/runtimes/linux-x64/native/libSkiaSharp.so`.

## Realistic expectations for capture size

**Skia serializes every typeface a picture references, per picture.** Uno's
default host loads Segoe UI + Fluent icon fonts + emoji fallbacks, and every
recorded frame embeds those glyph tables from scratch — `SKPicture` has no
inter-picture typeface sharing. On the Uno SamplesApp landing screen I
measured:

| frame | size    | notes                                              |
|-------|--------:|----------------------------------------------------|
| 0001  |  43 MB  | pre-content paint (fewer glyphs referenced)        |
| 0002+ | 179 MB+ | full sidebar rendered, every category name glyph'd |

`gzip -9` on the 43 MB frame gets it down to 17 MB — still too large for a
git commit.

For a git-committable capture, consider:

1. **Minimal repro app**: build a tiny Uno app with one Page containing only
   the primitives you want to benchmark (a Grid + a few Buttons + a Border).
   Its captured picture will be a few hundred KB, not tens of MB.
2. **Post-process**: write a small tool that deserializes the picture,
   iterates its ops, and re-records without the DrawTextBlob calls
   (or replaces them with rects of the same bounds). Loses text signal,
   keeps everything else.
3. **External hosting**: use git-LFS, an S3 bucket, or a GitHub Release for
   the captures. The `CapturedSceneRegistry` just scans local files, so
   any download-into-place tooling works.

The seed `Sample.GradientBlend.skp` in this directory is 478 bytes — proves
the plumbing without adding bulk. Real Uno captures should be produced
locally per developer, not committed to the tree.

## Other frameworks

The same trick works anywhere you can get at the `SKCanvas` a framework
draws into: swap it for `SKPictureRecorder`'s canvas, forward all calls,
call `EndRecording()` on some event, serialize. Avalonia, Blazor Skia hosts,
and MAUI Graphics-with-Skia are all candidates — contribution welcome.

## Verifying the plumbing without capturing

The `SkiaSharp.Benchmarks.Rendering.exe --record-scene <name> --output <path>`
subcommand serializes any existing `ISkiaScene` through `SKPictureRecorder`.
Useful for:

- Sanity-checking that a fresh benchmark build's playback path works end-to-end.
- Recording a hand-written scene as an `.skp` to compare direct-draw vs
  picture-playback cost of the same drawing (the delta is Skia's picture-replay
  overhead — usually negligible, but nice to have a number).

```
dotnet run -c Release --project benchmarks/SkiaSharp.Benchmarks.Rendering -- \
    --record-scene GradientBlend \
    --output benchmarks/SkiaSharp.Benchmarks.Rendering/Captures/Sample.GradientBlend.skp
```
