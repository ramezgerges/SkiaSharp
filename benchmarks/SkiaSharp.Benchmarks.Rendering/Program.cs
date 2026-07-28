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

		// `--decompile <input.skp> [--output <path.cs>] [--class-name <name>]`
		// — reverse an .skp back into a readable C# ISkiaScene stub. Handles
		// the op stream directly; resource tables (paints, paths, images,
		// text blobs) are emitted as indexed placeholders.
		if (Array.IndexOf(args, "--decompile") >= 0)
			return Decompile(args);

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
		for (var i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "--decompile") input = args[i + 1];
			else if (args[i] == "--output") output = args[i + 1];
			else if (args[i] == "--class-name") className = args[i + 1];
			else if (args[i] == "--partition" && int.TryParse(args[i + 1], out var pc)) partitionCount = pc;
		}
		if (input is null)
		{
			Console.Error.WriteLine("Usage: --decompile <input.skp> [--output <path.cs>] [--class-name <name>] [--partition <N>]");
			return 2;
		}
		if (partitionCount < 1)
		{
			Console.Error.WriteLine("--partition must be >= 1");
			return 2;
		}

		var bytes = File.ReadAllBytes(input);
		var source = partitionCount > 1
			? SkpDecompiler.DecompilePartitioned(bytes, className, partitionCount)
			: SkpDecompiler.Decompile(bytes, className);

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
}
