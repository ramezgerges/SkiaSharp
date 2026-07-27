using System;
using BenchmarkDotNet.Running;

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
		var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		foreach (var s in summaries)
			if (s.HasCriticalValidationErrors)
				return 2;
		return 0;
	}
}
