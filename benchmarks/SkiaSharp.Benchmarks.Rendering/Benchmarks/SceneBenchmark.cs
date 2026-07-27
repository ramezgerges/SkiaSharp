using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using SkiaSharp.Benchmarks.Rendering.Scenes;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// One BenchmarkDotNet row per <c>(Backend × Scene)</c>. Each iteration
/// runs a single Draw + Flush pair on a persistent surface that's created
/// once in <see cref="GlobalSetup"/> and disposed in <see cref="GlobalCleanup"/>.
///
/// <para>
/// BenchmarkDotNet's own warm-up + statistical loop handles pipeline JIT
/// costs and shader/pipeline compilation (Skia compiles pipelines lazily on
/// first use; subsequent frames reuse the cached ones). Look at
/// <c>WarmupIteration</c> vs <c>WorkloadIteration</c> in the output — the
/// warmup delta tells you how expensive the first draw of a scene was.
/// </para>
///
/// <para>
/// If a backend isn't available on this host — no Vulkan ICD, no Metal, no
/// WebGPU adapter — <see cref="GlobalSetup"/> throws
/// <see cref="BackendUnavailableException"/> and BenchmarkDotNet skips the
/// row rather than failing the run. The reason is included in the summary.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SceneBenchmark
{
	// Params are strings so backend/scene identities survive round-tripping
	// through BenchmarkDotNet's config/filter machinery (both use the ID form
	// `SceneBenchmark(Backend: "…", Scene: "…")`, easy to grep in results).
	[ParamsSource(nameof(BackendNames))]
	public string Backend { get; set; } = "raster";

	[ParamsSource(nameof(SceneNames))]
	public string Scene { get; set; } = "";

	public static IEnumerable<string> BackendNames => BackendCatalog.AllNames;

	// Two sources of scenes: reflected ISkiaScene implementations, and .skp
	// files captured from real UIs (see CapturedSceneRegistry). File-based
	// captures win a name collision, so a hand-written scene can be shadowed
	// by dropping its .skp equivalent into Captures/.
	public static IEnumerable<string> SceneNames =>
		CapturedSceneRegistry.AllNames.Concat(SceneCatalog.AllNames).Distinct();

	private IRenderBackend? _backend;
	private ISkiaScene? _scene;

	[GlobalSetup]
	public void GlobalSetup()
	{
		_backend = BackendCatalog.Create(Backend);
		if (!_backend.IsAvailable)
			throw new BackendUnavailableException(_backend.UnavailableReason ?? "backend unavailable");

		_scene = CapturedSceneRegistry.TryGet(Scene, out var captured)
			? captured
			: SceneCatalog.Get(Scene);
		_backend.Setup(_scene.Info);

		// One un-timed draw to force any lazy pipeline compilation into the
		// warm-up phase instead of the first timed iteration. Otherwise the
		// scenes with unique paint state each get their first draw counted as
		// a wildly-different outlier and BenchmarkDotNet's outlier removal
		// eats the signal.
		_backend.RenderFrame(_scene);
		_backend.Flush();
	}

	[GlobalCleanup]
	public void GlobalCleanup()
	{
		_backend?.Dispose();
	}

	[Benchmark]
	public void RenderScene()
	{
		_backend!.RenderFrame(_scene!);
		_backend.Flush();
	}
}
