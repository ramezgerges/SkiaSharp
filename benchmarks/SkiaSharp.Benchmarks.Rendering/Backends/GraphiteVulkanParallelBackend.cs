using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp.Tests;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// Graphite over Vulkan with per-partition parallel recording. Only kicks in
/// for scenes that implement <see cref="IPartitionedSkiaScene"/>; falls back
/// to <see cref="GraphiteVulkanBackend"/>-style sequential recording otherwise.
///
/// <para>
/// The parallel path exercises Graphite's headline architectural
/// differentiator: <c>Recorder</c>s are thread-local (each worker owns one
/// exclusively for the duration of its partition), but they all share the
/// single <c>SKGraphiteContext</c> and its resource cache. Each worker's
/// <c>Recording</c> is then <c>Insert</c>ed against the final destination
/// surface — <b>no intermediate offscreen buffers, no pixel copies</b>.
/// Ganesh cannot do this at all: a <c>GRContext</c> is single-threaded, so
/// the honest Ganesh comparison is the separate N-CPU-raster backend that
/// blits worker bitmaps at the end.
/// </para>
///
/// <para>
/// Reused across benchmark iterations: the Context, the per-worker
/// Recorders (one per partition slot), the scratch scene-sized Surfaces
/// each Recorder needs to have a canvas. Rebuilt per iteration: only the
/// Recordings themselves, which is the whole point (per-frame draw work).
/// </para>
/// </summary>
public sealed class GraphiteVulkanParallelBackend : IRenderBackend
{
	public string Name => "graphite-vulkan-parallel";

	public bool IsAvailable => UnavailableReason is null;

	public string? UnavailableReason =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? null
			: "Graphite-Vulkan is wired up for Linux and Windows only.";

	private SilkVkContext? _ctx;
	private SKGraphiteContext? _graphiteContext;
	private SKGraphiteVkBackendContext? _backendContext;
	private SKSurface? _finalSurface;

	// Per-worker state, sized to the scene's PartitionCount. Recorders and
	// their scratch surfaces are allocated in Setup and reused every frame.
	private SKGraphiteRecorder[] _workerRecorders = Array.Empty<SKGraphiteRecorder>();
	private SKSurface[] _workerScratchSurfaces = Array.Empty<SKSurface>();

	private readonly Dictionary<uint, SKImage> _textureImageCache = new();

	public void Setup(SKImageInfo info)
	{
		try { _ctx = new SilkVkContext(); }
		catch (Exception ex)
		{
			throw new BackendUnavailableException(
				"Vulkan bring-up failed (no ICD, or bad loader). Install Mesa Lavapipe (Linux) or a driver.", ex);
		}

		_backendContext = new SKGraphiteVkBackendContext
		{
			VkInstance = _ctx.Instance.Handle,
			VkPhysicalDevice = _ctx.PhysicalDevice.Handle,
			VkDevice = _ctx.Device.Handle,
			VkQueue = _ctx.GraphicsQueue.Handle,
			GraphicsQueueIndex = _ctx.GraphicsFamily,
			MaxApiVersion = SilkVkContext.ApiVersion,
			GetProcedureAddress = (name, instance, device) => _ctx.BaseGetProc(name, instance, device),
		};

		_graphiteContext = SKGraphiteContext.CreateVulkan(_backendContext)
			?? throw new BackendUnavailableException("SKGraphiteContext.CreateVulkan returned null.");

		// The main-thread Recorder + Surface used for the sequential fallback
		// (also serves as the destination Surface for parallel-path
		// Insert(TargetSurface=...) calls).
		var mainRecorder = _graphiteContext.CreateRecorder(-1, FindOrCreateCached)
			?? throw new BackendUnavailableException("SKGraphiteContext.CreateRecorder returned null.");
		_workerRecorders = new SKGraphiteRecorder[] { mainRecorder };
		_finalSurface = SKSurface.Create(mainRecorder, info)
			?? throw new BackendUnavailableException("SKSurface.Create(final) returned null.");
	}

	// Cache raster→texture uploads by (image identity, mipmapped) — same fix
	// as GraphiteVulkanBackend.
	private SKImage FindOrCreateCached(SKGraphiteRecorder recorder, SKImage image, bool mipmapped)
	{
		var key = image.UniqueId * 2u + (mipmapped ? 1u : 0u);
		if (!_textureImageCache.TryGetValue(key, out var cached))
		{
			cached = image.ToTextureImage(recorder, mipmapped);
			_textureImageCache[key] = cached;
		}
		return cached;
	}

	// Grow per-partition arrays lazily when a scene declares more slots than
	// we've already provisioned. Recorders are relatively cheap; scratch
	// surfaces are the size of the scene, so allocating them once per
	// benchmark run is fine.
	private void EnsureWorkerSlots(int count, SKImageInfo sceneInfo)
	{
		if (_workerRecorders.Length >= count + 1) return;

		var oldR = _workerRecorders;
		var newR = new SKGraphiteRecorder[count + 1];
		Array.Copy(oldR, newR, oldR.Length);
		for (var i = oldR.Length; i < newR.Length; i++)
			newR[i] = _graphiteContext!.CreateRecorder(-1, FindOrCreateCached)
				?? throw new InvalidOperationException("CreateRecorder returned null while provisioning worker.");
		_workerRecorders = newR;

		var oldS = _workerScratchSurfaces;
		var newS = new SKSurface[count];
		Array.Copy(oldS, newS, Math.Min(oldS.Length, newS.Length));
		for (var i = oldS.Length; i < newS.Length; i++)
			newS[i] = SKSurface.Create(_workerRecorders[i + 1], sceneInfo)
				?? throw new InvalidOperationException("SKSurface.Create(worker scratch) returned null.");
		_workerScratchSurfaces = newS;
	}

	public void RenderFrame(ISkiaScene scene)
	{
		if (_finalSurface is null || _graphiteContext is null)
			throw new InvalidOperationException("Setup was not called.");

		if (scene is IPartitionedSkiaScene partitioned)
			RenderParallel(partitioned);
		else
			scene.Draw(_finalSurface.Canvas); // sequential fallback
	}

	private void RenderParallel(IPartitionedSkiaScene scene)
	{
		var partitions = scene.PartitionCount;
		EnsureWorkerSlots(partitions, scene.Info);

		// Snap the recordings on each worker thread. Each thread owns one
		// Recorder + its scratch Surface exclusively for the duration of the
		// partition's Draw. All Recorders share the same Graphite Context,
		// so the resource cache (pipelines, uploaded textures) is common.
		var recordings = new SKGraphiteRecording?[partitions];
		Parallel.For(0, partitions, i =>
		{
			var recorder = _workerRecorders[i + 1];
			var canvas = _workerScratchSurfaces[i].Canvas;

			// Reset scratch state so state accumulated from the previous
			// iteration doesn't leak. Save-count invariant preserved: one
			// save around DrawPartition ensures the caller can freely
			// Save/Translate without leaking to next iteration.
			canvas.Save();
			try { scene.DrawPartition(canvas, i); }
			finally { canvas.Restore(); }

			recordings[i] = recorder.Snap();
		});

		// Insert every recording targeting the FINAL surface. No offscreen
		// blit — the recorded draws execute directly against the destination.
		for (var i = 0; i < partitions; i++)
		{
			var info = new SKGraphiteInsertRecordingInfo
			{
				Recording = recordings[i]!.Handle,
				TargetSurface = _finalSurface!.Handle,
			};
			if (_graphiteContext!.InsertRecording(info) != SKGraphiteInsertStatus.Success)
				throw new InvalidOperationException($"InsertRecording[{i}] did not report Success.");
		}

		for (var i = 0; i < partitions; i++)
			recordings[i]?.Dispose();
	}

	public void Flush()
	{
		if (_graphiteContext is null) throw new InvalidOperationException("Setup was not called.");

		// For a non-partitioned scene the sequential fallback recorded into
		// mainRecorder; we still need to Snap+Insert that.
		if (_workerRecorders.Length > 0)
		{
			using var mainRecording = _workerRecorders[0].Snap();
			if (mainRecording is not null)
			{
				var info = new SKGraphiteInsertRecordingInfo
				{
					Recording = mainRecording.Handle,
					TargetSurface = _finalSurface!.Handle,
				};
				_graphiteContext.InsertRecording(info);
			}
		}

		if (!_graphiteContext.Submit(new SKGraphiteSubmitInfo { Sync = true }))
			throw new InvalidOperationException("Submit(Sync=true) returned false.");
	}

	public void Dispose()
	{
		foreach (var img in _textureImageCache.Values) img.Dispose();
		_textureImageCache.Clear();

		foreach (var s in _workerScratchSurfaces) s?.Dispose();
		_workerScratchSurfaces = Array.Empty<SKSurface>();
		foreach (var r in _workerRecorders) r?.Dispose();
		_workerRecorders = Array.Empty<SKGraphiteRecorder>();

		_finalSurface?.Dispose(); _finalSurface = null;
		_graphiteContext?.Dispose(); _graphiteContext = null;
		_backendContext?.Dispose(); _backendContext = null;
		_ctx?.Dispose(); _ctx = null;
	}
}
