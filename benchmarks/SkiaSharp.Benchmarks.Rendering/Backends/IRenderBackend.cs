using System;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// One way of getting an <see cref="SKCanvas"/> that a scene can draw into,
/// paired with a Flush primitive that blocks until GPU work has actually
/// completed.
///
/// <para>
/// This is the benchmark-specific analogue of the visual-regression
/// <c>IRenderer</c>: same backends (raster, Ganesh over GL/Vulkan/Metal,
/// Graphite over Vulkan/Metal/Dawn), but without the pixel readback. The
/// intent is to measure draw-record-submit cost only; a real app never
/// reads its framebuffer back on every frame, so the mapAsync/copy path
/// would only add noise to the comparison we care about.
/// </para>
///
/// <para>
/// Lifecycle: <see cref="Setup"/> is called once per benchmark iteration
/// group (from <c>[GlobalSetup]</c>) and brings up the GPU context +
/// SKSurface. <see cref="RenderFrame"/> replays the scene into the same
/// surface. <see cref="Flush"/> submits and blocks on completion — this is
/// what defines the "frame boundary" for wall-clock measurement.
/// Implementations MUST make <see cref="IsAvailable"/> cheap and
/// side-effect-free (no context creation) so the benchmark harness can
/// skip unavailable rows without paying setup.
/// </para>
/// </summary>
internal interface IRenderBackend : IDisposable
{
	/// <summary>
	/// Stable identifier used as the row label in BenchmarkDotNet output
	/// (<c>raster</c>, <c>ganesh-vulkan</c>, <c>graphite-vulkan</c>, ...).
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Cheap probe: is this backend available on the current host? Must not
	/// bring up a GPU context. Return <see langword="false"/> when the OS,
	/// driver, or SDK is unreachable.
	/// </summary>
	bool IsAvailable { get; }

	/// <summary>
	/// Optional human-readable reason for <see cref="IsAvailable"/> being
	/// <see langword="false"/>. <see langword="null"/> when available.
	/// </summary>
	string? UnavailableReason { get; }

	/// <summary>
	/// Bring up the GPU context (if any) and allocate a persistent
	/// <see cref="SKSurface"/> sized to <paramref name="info"/>. Called
	/// once per benchmark run before iterations start. May throw
	/// <see cref="BackendUnavailableException"/> if the runtime probe fails
	/// (harness treats this as skip, not fail).
	/// </summary>
	void Setup(SKImageInfo info);

	/// <summary>
	/// Draw the scene into the surface allocated by <see cref="Setup"/>.
	/// Records draw commands only — does NOT submit; the caller pairs this
	/// with a subsequent <see cref="Flush"/> to define the frame boundary.
	/// </summary>
	void RenderFrame(ISkiaScene scene);

	/// <summary>
	/// Submit the recorded work and block until the GPU has finished. This
	/// is what the benchmark actually times: the cost of preparing +
	/// executing one frame's worth of draws. On raster backends this is a
	/// no-op after <see cref="RenderFrame"/> (the draw is synchronous on
	/// the CPU); on GPU backends this typically calls
	/// <c>GRContext.Flush + Submit(sync: true)</c> or Graphite's
	/// <c>context.Submit(new SKGraphiteSubmitInfo { Sync = true })</c>.
	/// </summary>
	void Flush();
}

/// <summary>
/// Thrown by <see cref="IRenderBackend.Setup"/> when the runtime probe
/// discovers the backend cannot come up on the current host (e.g. no
/// Vulkan ICD). The harness converts this to a BenchmarkDotNet skip.
/// </summary>
internal sealed class BackendUnavailableException : Exception
{
	public BackendUnavailableException(string reason) : base(reason) { }
	public BackendUnavailableException(string reason, Exception inner) : base(reason, inner) { }
}
