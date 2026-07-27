using System;
using System.Runtime.InteropServices;
using SkiaSharp.Tests;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// Graphite over Vulkan (Silk.NET). Mirrors <see cref="GaneshVulkanBackend"/>
/// on the same Vulkan bring-up, then feeds the handles to
/// <see cref="SKGraphiteContext.CreateVulkan"/>.
///
/// <para>
/// The Recorder + Context pair is kept alive across frames. A
/// <c>findOrCreate</c> image callback is registered so raster SkImages
/// upload to Graphite-backed textures on first use — otherwise scenes that
/// bake raster SkImages (text via cached glyph atlas, image draws) hit
/// "Couldn't convert SkImage to a Graphite-backed representation" and the
/// draws silently drop.
/// </para>
/// </summary>
public sealed class GraphiteVulkanBackend : IRenderBackend
{
	public string Name => "graphite-vulkan";

	public bool IsAvailable => UnavailableReason is null;

	public string? UnavailableReason =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? null
			: "Graphite-Vulkan is wired up for Linux and Windows only.";

	private SilkVkContext? _ctx;
	private SKGraphiteContext? _graphiteContext;
	private SKGraphiteRecorder? _recorder;
	private SKGraphiteVkBackendContext? _backendContext;
	private SKSurface? _surface;

	public void Setup(SKImageInfo info)
	{
		try
		{
			_ctx = new SilkVkContext();
		}
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

		// Raster→Graphite image upload on first use; matches Uno's WebGpuBrowserRenderer.
		_recorder = _graphiteContext.CreateRecorder(-1, (recorder, image, mipmapped) => image.ToTextureImage(recorder, mipmapped))
			?? throw new BackendUnavailableException("SKGraphiteContext.CreateRecorder returned null.");

		_surface = SKSurface.Create(_recorder, info)
			?? throw new BackendUnavailableException("SKSurface.Create(graphite-vulkan) returned null.");
	}

	public void RenderFrame(ISkiaScene scene)
	{
		if (_surface is null)
			throw new InvalidOperationException("Setup was not called.");
		scene.Draw(_surface.Canvas);
	}

	public void Flush()
	{
		// Graphite: Snap the recorded work into a Recording, hand it to the
		// context, then submit synchronously so the GPU has actually finished
		// before the timer stops.
		using var recording = _recorder!.Snap()
			?? throw new InvalidOperationException("Recorder.Snap() returned null.");
		if (_graphiteContext!.InsertRecording(recording) != SKGraphiteInsertStatus.Success)
			throw new InvalidOperationException("InsertRecording did not report Success.");
		if (!_graphiteContext.Submit(new SKGraphiteSubmitInfo { Sync = true }))
			throw new InvalidOperationException("Submit(Sync=true) returned false.");
	}

	public void Dispose()
	{
		_surface?.Dispose();
		_surface = null;
		_recorder?.Dispose();
		_recorder = null;
		_graphiteContext?.Dispose();
		_graphiteContext = null;
		_backendContext?.Dispose();
		_backendContext = null;
		_ctx?.Dispose();
		_ctx = null;
	}
}
