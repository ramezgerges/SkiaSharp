#nullable disable

#if !USE_DELEGATES
using System;

namespace SkiaSharp
{
	public class GraphiteRecording : SKObject, ISKSkipObjectRegistration
	{
		internal GraphiteRecording (IntPtr handle, bool owns)
			: base (handle, owns)
		{
		}

		protected override void DisposeNative () =>
			SkiaApi.graphite_recording_unref (Handle);

		internal static GraphiteRecording GetObject (IntPtr handle) =>
			handle == IntPtr.Zero ? null : new GraphiteRecording (handle, true);
	}
}
#endif
