using System;
using System.IO;
using BenchmarkDotNet.Running;
using SkiaSharp.Benchmarks.Rendering.Tools;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

// Ganesh-vs-Graphite rendering benchmarks. Each (backend × scene) pair renders
// the same ISkiaScene into a fixed-size offscreen surface, with GPU work
// blocked-on before recording the frame time. No pixel readback — the intent is
// to measure the draw-and-submit cost, not the map/copy path.
//
// Backends live in Backends/, scenes are shared with the visual-regression
// matrix under tests/Tests/SkiaSharp/Visual/Scenes/ so both suites use the same
// curated drawing set.
//
// Run:
//   dotnet run -c Release --project benchmarks/SkiaSharp.Benchmarks.Rendering
//     --                                       # BenchmarkSwitcher args below
//     --filter *SceneBenchmark*                # pick a benchmark class
//     --list flat                              # enumerate configurations
//     -a BENCH_RESULTS/2026-07-25              # artifacts dir
//
// The --filter form is BenchmarkDotNet's ID glob; use `--list flat` first to
// see every (backend, scene) row and copy an ID from it.
internal static class Program
{
	private static int Main(string[] args)
	{
		// `--record-scene <name> --output <path>` — record an existing ISkiaScene
		// through an SKPictureRecorder and serialize the picture to disk. Useful
		// as a smoke test for the playback path and for turning any hand-written
		// scene into a captured-picture scene to compare direct-draw vs
		// picture-playback cost of the same drawing.
		if (Array.IndexOf(args, "--record-scene") >= 0)
			return RecordScene(args);

		// `--decompile <input.skp> [--output <path.cs>] [--class-name <name>] [--partition N]`
		// — reverse an .skp back into a readable C# ISkiaScene stub. Handles
		// the op stream directly; resource tables (paints, paths, images,
		// text blobs) are emitted as indexed placeholders.
		if (Array.IndexOf(args, "--decompile") >= 0)
			return Decompile(args);

		// `--render-skp <input.skp> --output <path.png> [--size WxH]` —
		// deserialize a captured .skp and rasterise it to PNG. Baseline
		// reference for eyeballing whether our decompiled C# reproduces
		// the original picture.
		if (Array.IndexOf(args, "--render-skp") >= 0)
			return RenderSkp(args);

		// `--render-scene <name> --output <path.png> [--size WxH]` — draw a
		// discovered ISkiaScene onto a raster surface and save as PNG.
		// Same signature/output-format as `--render-skp`; use both to
		// compare decompiler output against its source .skp side by side.
		if (Array.IndexOf(args, "--render-scene") >= 0)
			return RenderScene(args);

		if (Array.IndexOf(args, "--sample-pixels") >= 0)
		{
			// Debug helper: grid-sample RGBA at several points so we can eye
			// pixel-level differences between reference and generated PNGs
			// without loading Pillow or an image viewer.
			var path = args[Array.IndexOf(args, "--sample-pixels") + 1];
			using var img = SKImage.FromEncodedData(path);
			using var bmp = SKBitmap.FromImage(img);
			Console.WriteLine($"colorType={bmp.Info.ColorType} alphaType={bmp.Info.AlphaType} size={bmp.Info.Width}x{bmp.Info.Height}");
			int cw = bmp.Info.Width, ch = bmp.Info.Height;
			for (var y = 40; y < ch; y += ch / 6)
			{
				var line = new System.Text.StringBuilder($"y={y,3}: ");
				for (var x = 40; x < cw; x += cw / 6)
				{
					var c = bmp.GetPixel(x, y);
					line.Append($"({x,4},{y,3})=({c.Red,3},{c.Green,3},{c.Blue,3},{c.Alpha,3}) ");
				}
				Console.WriteLine(line);
			}
			return 0;
		}

		var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		foreach (var s in summaries)
			if (s.HasCriticalValidationErrors)
				return 2;
		return 0;
	}

	private static int RecordScene(string[] args)
	{
		string? sceneName = null;
		string? output = null;
		for (var i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "--record-scene") sceneName = args[i + 1];
			else if (args[i] == "--output") output = args[i + 1];
		}
		if (sceneName is null || output is null)
		{
			Console.Error.WriteLine("Usage: --record-scene <scene-name> --output <path.skp>");
			return 2;
		}

		var scene = SceneCatalog.Get(sceneName);
		using var recorder = new SKPictureRecorder();
		var cull = new SKRect(0, 0, scene.Info.Width, scene.Info.Height);
		var canvas = recorder.BeginRecording(cull);
		scene.Draw(canvas);
		using var picture = recorder.EndRecording();

		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
		using var data = picture.Serialize();
		using var stream = File.Create(output);
		data.SaveTo(stream);

		Console.Out.WriteLine($"Recorded '{sceneName}' → {output} ({data.Size:N0} bytes)");
		return 0;
	}

	private static int Decompile(string[] args)
	{
		string? input = null;
		string? output = null;
		string className = "DecompiledScene";
		int partitionCount = 1;
		bool embedFontData = true;
		for (var i = 0; i < args.Length; i++)
		{
			if (args[i] == "--no-font-data") { embedFontData = false; continue; }
			if (i + 1 >= args.Length) continue;
			if (args[i] == "--decompile") input = args[i + 1];
			else if (args[i] == "--output") output = args[i + 1];
			else if (args[i] == "--class-name") className = args[i + 1];
			else if (args[i] == "--partition" && int.TryParse(args[i + 1], out var pc)) partitionCount = pc;
		}
		if (input is null)
		{
			Console.Error.WriteLine("Usage: --decompile <input.skp> [--output <path.cs>] [--class-name <name>] [--partition <N>] [--no-font-data]");
			return 2;
		}
		if (partitionCount < 1)
		{
			Console.Error.WriteLine("--partition must be >= 1");
			return 2;
		}

		var bytes = File.ReadAllBytes(input);
		var source = partitionCount > 1
			? SkpDecompiler.DecompilePartitioned(bytes, className, partitionCount, embedFontData)
			: SkpDecompiler.Decompile(bytes, className, embedFontData);

		if (output is null)
		{
			Console.Out.Write(source);
		}
		else
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
			File.WriteAllText(output, source);
			Console.Out.WriteLine($"Decompiled '{input}' → {output} ({source.Length:N0} chars)");
		}
		return 0;
	}

	private static int RenderSkp(string[] args)
	{
		string? input = null;
		string? output = null;
		var size = (Width: 1024, Height: 640);
		for (var i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "--render-skp") input = args[i + 1];
			else if (args[i] == "--output") output = args[i + 1];
			else if (args[i] == "--size" && TryParseSize(args[i + 1], out var s)) size = s;
		}
		if (input is null || output is null)
		{
			Console.Error.WriteLine("Usage: --render-skp <input.skp> --output <path.png> [--size WxH]");
			return 2;
		}

		var bytes = File.ReadAllBytes(input);
		using var data = SKData.CreateCopy(bytes);
		using var picture = SKPicture.Deserialize(data);
		if (picture is null)
		{
			Console.Error.WriteLine($"SKPicture.Deserialize returned null for '{input}'");
			return 3;
		}

		using var surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
			?? throw new InvalidOperationException("SKSurface.Create returned null");
		surface.Canvas.Clear(SKColors.White);
		surface.Canvas.DrawPicture(picture);
		SaveSurfaceToPng(surface, output);
		Console.Out.WriteLine($"Rendered '{input}' → {output} ({size.Width}x{size.Height})");
		return 0;
	}

	private static int RenderScene(string[] args)
	{
		string? sceneName = null;
		string? output = null;
		(int, int)? sizeOverride = null;
		for (var i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "--render-scene") sceneName = args[i + 1];
			else if (args[i] == "--output") output = args[i + 1];
			else if (args[i] == "--size" && TryParseSize(args[i + 1], out var s)) sizeOverride = s;
		}
		if (sceneName is null || output is null)
		{
			Console.Error.WriteLine("Usage: --render-scene <scene-name> --output <path.png> [--size WxH]");
			return 2;
		}

		var scene = SceneCatalog.Get(sceneName);
		var info = sizeOverride is { } so
			? new SKImageInfo(so.Item1, so.Item2, SKColorType.Rgba8888, SKAlphaType.Premul)
			: scene.Info;

		using var surface = SKSurface.Create(info)
			?? throw new InvalidOperationException("SKSurface.Create returned null");
		surface.Canvas.Clear(SKColors.White);
		scene.Draw(surface.Canvas);
		SaveSurfaceToPng(surface, output);
		Console.Out.WriteLine($"Rendered scene '{sceneName}' → {output} ({info.Width}x{info.Height})");
		return 0;
	}

	private static void SaveSurfaceToPng(SKSurface surface, string output)
	{
		using var img = surface.Snapshot();
		using var pngData = img.Encode(SKEncodedImageFormat.Png, 100)
			?? throw new InvalidOperationException("Snapshot.Encode(PNG) returned null");
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
		using var f = File.Create(output);
		pngData.SaveTo(f);
	}

	private static bool TryParseSize(string s, out (int Width, int Height) size)
	{
		size = default;
		var parts = s.Split(new[] { 'x', 'X' }, 2);
		if (parts.Length != 2) return false;
		if (!int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h)) return false;
		size = (w, h);
		return true;
	}
}
