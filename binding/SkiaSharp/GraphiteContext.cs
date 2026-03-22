#nullable disable

#if !USE_DELEGATES
using System;

namespace SkiaSharp
{
	public unsafe class GraphiteContext : SKObject, ISKSkipObjectRegistration
	{
		internal GraphiteContext (IntPtr handle, bool owns)
			: base (handle, owns)
		{
		}

		protected override void DisposeNative () =>
			SkiaApi.graphite_context_unref (Handle);

		// CreateVulkan

		public static GraphiteContext CreateVulkan (GRVkBackendContext backendContext) =>
			CreateVulkan (backendContext, null);

		public static GraphiteContext CreateVulkan (GRVkBackendContext backendContext, GraphiteContextOptions options)
		{
			if (backendContext == null)
				throw new ArgumentNullException (nameof (backendContext));

			var native = backendContext.ToNative ();

			if (options == null) {
				return GetObject (SkiaApi.graphite_context_make_vulkan (&native));
			} else {
				var opts = options.ToNative ();
				return GetObject (SkiaApi.graphite_context_make_vulkan_with_options (&native, &opts));
			}
		}

		// Properties

		public bool IsDeviceLost =>
			SkiaApi.graphite_context_is_device_lost (Handle);

		public int MaxTextureSize =>
			SkiaApi.graphite_context_max_texture_size (Handle);

		public long CurrentBudgetedBytes =>
			(long)SkiaApi.graphite_context_current_budgeted_bytes (Handle);

		public long MaxBudgetedBytes =>
			(long)SkiaApi.graphite_context_max_budgeted_bytes (Handle);

		// Recorder

		public GraphiteRecorder MakeRecorder () =>
			GraphiteRecorder.GetObject (SkiaApi.graphite_context_make_recorder (Handle));

		// Submission

		public bool InsertRecording (GraphiteRecording recording)
		{
			if (recording == null)
				throw new ArgumentNullException (nameof (recording));
			return SkiaApi.graphite_context_insert_recording (Handle, recording.Handle);
		}

		public bool Submit (bool syncToCpu = false) =>
			SkiaApi.graphite_context_submit (Handle, syncToCpu);

		// Resource management

		public void FreeGpuResources () =>
			SkiaApi.graphite_context_free_gpu_resources (Handle);

		internal static GraphiteContext GetObject (IntPtr handle) =>
			handle == IntPtr.Zero ? null : new GraphiteContext (handle, true);
	}
}
#endif
