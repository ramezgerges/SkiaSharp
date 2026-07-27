using System;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// CPU raster baseline. No GPU context; SKSurface.Create allocates a
/// straight-up pixmap. Flush is a no-op — draws are synchronous on the
/// managed thread — so the reported wall time is purely the cost of
/// scene.Draw + Skia's software rasterizer.
///
/// <para>
/// Always available. Its main value is grounding the GPU-backend numbers:
/// when Graphite/Vulkan renders scene X in 30% of Raster's time, that's the
/// GPU actually paying off; if a GPU backend is <em>slower</em> than Raster,
/// the scene is small enough that submission overhead dominates and you're
/// measuring driver ceremony, not throughput.
/// </para>
/// </summary>
public sealed class RasterBackend : IRenderBackend
{
	public string Name => "raster";

	public bool IsAvailable => true;

	public string? UnavailableReason => null;

	private SKSurface? _surface;

	public void Setup(SKImageInfo info)
	{
		_surface = SKSurface.Create(info)
			?? throw new BackendUnavailableException("SKSurface.Create(raster) returned null.");
	}

	public void RenderFrame(ISkiaScene scene)
	{
		if (_surface is null)
			throw new InvalidOperationException("Setup was not called.");
		scene.Draw(_surface.Canvas);
	}

	public void Flush()
	{
		// Raster draws are already resolved on the CPU by the time RenderFrame
		// returns. No queued work to await.
	}

	public void Dispose()
	{
		_surface?.Dispose();
		_surface = null;
	}
}
