#nullable disable

#if !USE_DELEGATES
using System;

namespace SkiaSharp
{
	public unsafe class GraphiteRecorder : SKObject, ISKSkipObjectRegistration
	{
		internal GraphiteRecorder (IntPtr handle, bool owns)
			: base (handle, owns)
		{
		}

		protected override void DisposeNative () =>
			SkiaApi.graphite_recorder_unref (Handle);

		public SKSurface MakeRenderTarget (SKImageInfo imageInfo, bool mipmapped = false)
		{
			var cinfo = SKImageInfoNative.FromManaged (ref imageInfo);
			return SKSurface.GetObject (SkiaApi.graphite_recorder_make_render_target (Handle, &cinfo, mipmapped));
		}

		public GraphiteRecording Snap () =>
			GraphiteRecording.GetObject (SkiaApi.graphite_recorder_snap (Handle));

		public void FreeGpuResources () =>
			SkiaApi.graphite_recorder_free_gpu_resources (Handle);

		internal static GraphiteRecorder GetObject (IntPtr handle) =>
			handle == IntPtr.Zero ? null : new GraphiteRecorder (handle, true);
	}
}
#endif
