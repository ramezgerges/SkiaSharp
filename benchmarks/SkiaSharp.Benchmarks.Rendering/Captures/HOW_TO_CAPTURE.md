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

## Uno.Gallery: capturing specific sample pages (validated 2026-07-28)

xdotool nav is unreliable under Xvfb — Uno's X11 host doesn't reliably
consume synthesized input events. Cleanest workaround: patch
`Uno.Gallery/App.xaml.cs` to auto-navigate on startup, driven by an env
var. About 20 lines. Add this at the bottom of `OnLaunchedOrActivated`:

```csharp
var pageName = Environment.GetEnvironmentVariable("UNO_GALLERY_PAGE");
if (!string.IsNullOrEmpty(pageName))
{
    _ = MainWindow.DispatcherQueue.TryEnqueue(async () =>
    {
        await System.Threading.Tasks.Task.Delay(2000);
        try
        {
            var shell = GetWindowShell(MainWindow);
            var t = System.Reflection.Assembly.GetExecutingAssembly()
                .GetTypes()
                .FirstOrDefault(x => x.Name == pageName
                    && typeof(Microsoft.UI.Xaml.Controls.Page).IsAssignableFrom(x));
            if (t is null) { this.Log().Warn($"Page '{pageName}' not found."); return; }
            var page = (Microsoft.UI.Xaml.Controls.Page)Activator.CreateInstance(t)!;
            shell.NavigationView.Content = page;
        }
        catch (Exception ex) { this.Log().Warn($"UNO_GALLERY_PAGE nav failed: {ex}"); }
    });
}
```

Now each launch renders a specific sample:

```bash
DISPLAY=:99 UNO_GALLERY_PAGE=ButtonSamplePage    UNO_DUMP_SKPICTURE_DIR=/tmp/buttons \
    dotnet Uno.Gallery.dll
DISPLAY=:99 UNO_GALLERY_PAGE=ColorPickerSamplePage UNO_DUMP_SKPICTURE_DIR=/tmp/color \
    dotnet Uno.Gallery.dll
DISPLAY=:99 UNO_GALLERY_PAGE=AcrylicSamplePage     UNO_DUMP_SKPICTURE_DIR=/tmp/acrylic \
    dotnet Uno.Gallery.dll
```

92 sample pages available under `Views/SamplePages/*SamplePage.xaml.cs` —
enumerate the file names to see what's shippable.

**Timings on 4 captured pages** (lavapipe software Vulkan, so directional):

| Uno.Gallery page  | raster    | ganesh-vk | graphite-vk |
|-------------------|----------:|----------:|------------:|
| Overview          | 11.35 ms  |  11.65 ms |     80.1 ms |
| ButtonSample      |  7.47 ms  |   8.33 ms |     62.0 ms |
| ColorPickerSample |  8.06 ms  |   8.68 ms |     63.1 ms |
| AcrylicSample     |**164.5 ms** | **13.7 ms** |     77.9 ms |
| CalendarView      |  7.54 ms  |   8.42 ms |     61.1 ms |

Two patterns to notice:

- **AcrylicSample is the smoking-gun real-world Graphite regression.**
  It has a real backdrop-blur — 12× slower on raster than Ganesh
  (backdrop filter is the classic GPU-pays-off workload). Ganesh runs
  it at 14 ms; **Graphite lags at 78 ms**, 6× slower than Ganesh on
  the exact primitive the SKGraphite backend should excel at.
  Matches the synthetic `RRectBlur` (24×) / `SuperellipseBlur` (34×)
  / `BackdropBlur` regressions surfaced earlier.
- **All 4 non-Acrylic pages: raster ≈ ganesh << graphite.** Uno's
  text-heavy, save/setmatrix/clip-heavy render sits in a sweet spot
  where raster keeps up with GPU, and Graphite consistently costs
  7–8× more than Ganesh. Also matches the synthetic-scene pattern.

Screenshots of each captured page are committed alongside this doc as
`UnoGallery.<page>.png` — treat them as visual reference for what the
captures actually contain. The `.skp` files themselves are 200–450 MB
apiece and not committed; reproduce them locally with the recipe above.



Cross-repo shortcut that avoids rebuilding Uno.Sdk locally: let Uno.Gallery
restore normally from its published NuGet SDK, then hot-swap in your local
Uno + SkiaSharp DLLs. Works when the version drift is small — align the
`Uno.Sdk` pin in Gallery's `global.json` to whatever your local Uno source
was branched from.

Validated with:
- `/workspace/uno` on `6.5-release-branch-cut` + a few commits
- `Uno.Gallery` on `Uno.Sdk 6.5.36` (closest matching NuGet release)

```bash
# 1. Match Gallery's SDK to what your local Uno is closest to.
sed -i 's/"Uno.Sdk": ".*"/"Uno.Sdk": "6.5.36"/' Uno.Gallery/global.json
rm -rf Uno.Gallery/Uno.Gallery/bin Uno.Gallery/Uno.Gallery/obj

# 2. Restore + build the desktop head against the published Uno NuGets.
dotnet build Uno.Gallery/Uno.Gallery/Uno.Gallery.csproj \
    -c Release -p:TargetFrameworkOverride=net10.0-desktop -p:NuGetAudit=false

# 3. Hot-swap in your patched local Uno + SkiaSharp DLLs. Do them ALL —
#    partial swaps trip ITypeXxxExtension / MissingMethod cascades.
GAL=Uno.Gallery/Uno.Gallery/bin/Release/net10.0-desktop
LOCAL_UNO=/path/to/uno/src

for local_dll in $(find $LOCAL_UNO -path "*Skia*/Release/net10.0/*.dll" -not -path "*/obj/*"); do
    name=$(basename "$local_dll")
    [ -f "$GAL/$name" ] && cp -u "$local_dll" "$GAL/$name"
done

# SkiaSharp side (the version-guard tripwire is real — do all four).
LOCAL_SKIA=/path/to/skiasharp
cp $LOCAL_SKIA/output/native/linux-x64/libSkiaSharp.so \
    $GAL/runtimes/linux-x64/native/libSkiaSharp.so
for local_dll in $(find $LOCAL_SKIA -path "*/bin/Release/net10.0/SkiaSharp*.dll" -not -path "*/obj/*"); do
    name=$(basename "$local_dll")
    [ -f "$GAL/$name" ] && cp -u "$local_dll" "$GAL/$name"
done

# 4. Run under Xvfb + fluxbox.
rm -f /tmp/.X99-lock
Xvfb :99 -screen 0 1024x640x24 -ac -nolisten tcp &
DISPLAY=:99 fluxbox >/dev/null 2>&1 &
sleep 1
DISPLAY=:99 UNO_DUMP_SKPICTURE_DIR=/tmp/uno-caps \
    dotnet $GAL/Uno.Gallery.dll
```

Uno.Gallery's Overview page (screenshot: `UnoGallery.Overview.png` in this
directory) rendered end-to-end, sidebar + isometric hero illustration +
Material/Fluent/Cupertino theme tabs.

**Playback timings on the captured frame** (lavapipe software Vulkan, so
directional only — real GPU expected to flip Ganesh and Graphite well
below raster):

| Backend         | Mean      | vs Ganesh  |
|-----------------|----------:|-----------:|
| raster          | 11.35 ms  | –          |
| ganesh-vulkan   | 11.65 ms  | 1×         |
| graphite-vulkan | **80.07 ms** | **6.9× slower** |

Graphite lagging on a real Uno UI matches the pattern the synthetic
`RRectBlur` / `SuperellipseBlur` / `PictureCache` benchmarks surfaced
earlier — worth checking whether it holds on discrete-GPU hardware.

**Op mix of that single Overview frame** (from `--decompile` — first
60 lines saved as `UnoGallery.Overview.decompiled.cs.txt`):

```
    205 canvas.Save
    205 canvas.Restore
    162 canvas.SetMatrix
     56 canvas.ClipRoundRect
     48 canvas.DrawText
     16 canvas.ClipRect
      3 // op DRAW_PICTURE_MATRIX_PAINT (nested subpictures)
      1 canvas.DrawPaint
      1 // op CLIP_PATH
```

1866 lines of readable C# in total — you can see the entire Uno layout as
nested Save / SetMatrix / clip / draw pyramids.

## Headless capture on Linux, SamplesApp variant (validated 2026-07-28)

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
