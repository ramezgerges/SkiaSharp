using System;
using System.Runtime.InteropServices;
using SkiaSharp.Tests;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// Ganesh over Vulkan (Silk.NET). Brings Vulkan up headless via
/// <see cref="SilkVkContext"/> — the same helper the Vulkan visual tests
/// use — and hands the raw handles to <see cref="GRContext.CreateVulkan"/>
/// via <see cref="GRVkBackendContext"/>.
///
/// <para>
/// Availability probe checks OS + Vulkan loader reachability without
/// bringing the context up. If <c>vulkan-1.dll</c> / <c>libvulkan.so</c>
/// isn't installed the probe fails cheaply and the harness skips.
/// </para>
/// </summary>
public sealed class GaneshVulkanBackend : IRenderBackend
{
	public string Name => "ganesh-vulkan";

	public bool IsAvailable => UnavailableReason is null;

	public string? UnavailableReason =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? null
			: "Ganesh-Vulkan is wired up for Linux and Windows only.";

	private SilkVkContext? _ctx;
	private GRVkBackendContext? _backendContext;
	private GRContext? _grContext;
	private SKSurface? _surface;

	public unsafe void Setup(SKImageInfo info)
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

		_backendContext = new GRVkBackendContext
		{
			VkInstance     = _ctx.Instance.Handle,
			VkPhysicalDevice = _ctx.PhysicalDevice.Handle,
			VkDevice       = _ctx.Device.Handle,
			VkQueue        = _ctx.GraphicsQueue.Handle,
			GraphicsQueueIndex = _ctx.GraphicsFamily,
			MaxAPIVersion  = SilkVkContext.ApiVersion,
			GetProcedureAddress = (name, instance, device) => _ctx.BaseGetProc(name, instance, device),
		};

		_grContext = GRContext.CreateVulkan(_backendContext)
			?? throw new BackendUnavailableException("GRContext.CreateVulkan returned null.");

		_surface = SKSurface.Create(_grContext, budgeted: true, info)
			?? throw new BackendUnavailableException("SKSurface.Create(ganesh-vulkan) returned null.");
	}

	public void RenderFrame(ISkiaScene scene)
	{
		if (_surface is null)
			throw new InvalidOperationException("Setup was not called.");
		scene.Draw(_surface.Canvas);
	}

	public void Flush()
	{
		// Ganesh: enqueue commands and block until the queue has drained.
		// FlushAndSubmit(syncCpu: true) is the sync-CPU variant.
		_grContext!.Flush();
		_grContext.Submit(synchronous: true);
	}

	public void Dispose()
	{
		_surface?.Dispose();
		_surface = null;
		_grContext?.Dispose();
		_grContext = null;
		_backendContext?.Dispose();
		_backendContext = null;
		_ctx?.Dispose();
		_ctx = null;
	}
}
