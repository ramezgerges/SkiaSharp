using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SkiaSharp.Tests;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// The honest "what if you tried to parallelise with Ganesh?" reference.
/// Ganesh's <c>GRContext</c> is single-threaded, so this backend spawns N
/// CPU raster surfaces (one per partition, drawn on a worker thread), then
/// on the main thread uploads each worker's bitmap as an <c>SKImage</c> and
/// composites them into the final Ganesh-Vulkan surface.
///
/// <para>
/// That's what parallel-rasterisation systems have looked like historically
/// (Chromium's tile rasteriser pre-Graphite; Firefox's WR tile workers).
/// It works — but the CPU→GPU upload + composite step is the pixel-copy
/// cost the <c>GraphiteVulkanParallelBackend</c> avoids entirely. The
/// number to watch is not just wall-clock but the delta between
/// <c>graphite-vulkan-parallel</c> and this backend: it's the value of
/// "no intermediate buffers + shared resource pool".
/// </para>
///
/// <para>
/// For non-partitioned scenes: falls back to plain sequential Ganesh so
/// the benchmark matrix stays sane.
/// </para>
/// </summary>
public sealed class GaneshVulkanNContextsBackend : IRenderBackend
{
	public string Name => "ganesh-vulkan-nctx";

	public bool IsAvailable => UnavailableReason is null;

	public string? UnavailableReason =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? null
			: "Ganesh-Vulkan is wired up for Linux and Windows only.";

	private SilkVkContext? _ctx;
	private GRContext? _grContext;
	private SKSurface? _finalSurface;

	// Per-worker raster bitmap surfaces. Reused across iterations.
	private SKSurface[] _workerRasterSurfaces = Array.Empty<SKSurface>();

	public void Setup(SKImageInfo info)
	{
		try { _ctx = new SilkVkContext(); }
		catch (Exception ex)
		{
			throw new BackendUnavailableException(
				"Vulkan bring-up failed (no ICD, or bad loader). Install Mesa Lavapipe (Linux) or a driver.", ex);
		}

		var backend = new GRVkBackendContext
		{
			VkInstance = _ctx.Instance.Handle,
			VkPhysicalDevice = _ctx.PhysicalDevice.Handle,
			VkDevice = _ctx.Device.Handle,
			VkQueue = _ctx.GraphicsQueue.Handle,
			GraphicsQueueIndex = _ctx.GraphicsFamily,
			MaxAPIVersion = SilkVkContext.ApiVersion,
			GetProcedureAddress = (name, instance, device) => _ctx.BaseGetProc(name, instance, device),
		};

		_grContext = GRContext.CreateVulkan(backend)
			?? throw new BackendUnavailableException("GRContext.CreateVulkan returned null.");

		_finalSurface = SKSurface.Create(_grContext, budgeted: true, info)
			?? throw new BackendUnavailableException("SKSurface.Create(final) returned null.");
	}

	private void EnsureWorkerSurfaces(int count, SKImageInfo sceneInfo)
	{
		if (_workerRasterSurfaces.Length >= count) return;
		var old = _workerRasterSurfaces;
		var next = new SKSurface[count];
		Array.Copy(old, next, old.Length);
		for (var i = old.Length; i < next.Length; i++)
			next[i] = SKSurface.Create(sceneInfo)
				?? throw new InvalidOperationException("SKSurface.Create(raster worker) returned null.");
		_workerRasterSurfaces = next;
	}

	public void RenderFrame(ISkiaScene scene)
	{
		if (_finalSurface is null || _grContext is null)
			throw new InvalidOperationException("Setup was not called.");

		if (scene is IPartitionedSkiaScene partitioned)
			RenderNContexts(partitioned);
		else
			scene.Draw(_finalSurface.Canvas);
	}

	private void RenderNContexts(IPartitionedSkiaScene scene)
	{
		var n = scene.PartitionCount;
		EnsureWorkerSurfaces(n, scene.Info);

		// Fresh transparent background per worker each iteration so previous
		// pixels don't linger. This is a real cost of the N-contexts model
		// that Graphite's retarget-on-insert doesn't pay.
		for (var i = 0; i < n; i++)
			_workerRasterSurfaces[i].Canvas.Clear(SKColors.Transparent);

		Parallel.For(0, n, i => scene.DrawPartition(_workerRasterSurfaces[i].Canvas, i));

		// Composite worker outputs onto the final GPU surface.
		// Each worker's Snapshot is a CPU-backed SKImage — DrawImage against a
		// Ganesh canvas will trigger a CPU→GPU upload for the first draw. That
		// upload cost is the "blit" that the Graphite parallel path avoids.
		var canvas = _finalSurface!.Canvas;
		using var samplingPaint = new SKPaint();
		for (var i = 0; i < n; i++)
		{
			using var snapshot = _workerRasterSurfaces[i].Snapshot();
			canvas.DrawImage(snapshot, 0, 0, SKSamplingOptions.Default, samplingPaint);
		}
	}

	public void Flush()
	{
		_grContext?.Flush();
		_grContext?.Submit(synchronous: true);
	}

	public void Dispose()
	{
		foreach (var s in _workerRasterSurfaces) s?.Dispose();
		_workerRasterSurfaces = Array.Empty<SKSurface>();
		_finalSurface?.Dispose(); _finalSurface = null;
		_grContext?.Dispose(); _grContext = null;
		_ctx?.Dispose(); _ctx = null;
	}
}
