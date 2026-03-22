#nullable disable

using System;
using System.Runtime.InteropServices;

namespace SkiaSharp
{
	public class GraphiteContextOptions
	{
		public long GpuBudgetInBytes { get; set; } = 256 * 1024 * 1024;
		public int InternalMultisampleCount { get; set; } = 4;
		public bool DisableDriverCorrectnessWorkarounds { get; set; } = false;

		internal GraphiteContextOptionsNative ToNative () =>
			new GraphiteContextOptionsNative {
				fGpuBudgetInBytes = (IntPtr)GpuBudgetInBytes,
				fInternalMultisampleCount = InternalMultisampleCount,
				fDisableDriverCorrectnessWorkarounds = DisableDriverCorrectnessWorkarounds ? (byte)1 : (byte)0,
			};
	}

	[StructLayout (LayoutKind.Sequential)]
	internal unsafe struct GraphiteContextOptionsNative
	{
		public IntPtr fGpuBudgetInBytes;
		public int fInternalMultisampleCount;
		public byte fDisableDriverCorrectnessWorkarounds;
	}
}
