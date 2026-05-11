using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using SkiaSharp;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Tests.RenderHost.Wasm;

// In-page renderers, one per backend. Caller passes a scene + size; we
// return raw RGBA8888/Premul pixels.
//
// raster      — pure CPU, always works
// graphite-dawn — WebGPU via Skia Graphite, no canvas (the JS bridge's
//                 initOffscreenAsync gives us device+queue handles; Skia
//                 allocates its own GPUTexture inside the recorder)
internal static partial class WasmRenderers
{
	public static byte[] RenderRaster (ISkiaScene scene, SKImageInfo info)
	{
		using var surface = SKSurface.Create (info)
			?? throw new InvalidOperationException ("SKSurface.Create returned null for raster");
		scene.Draw (surface.Canvas);
		return ReadRgbaPremul (surface, info);
	}

	// Lazy: created on first call, kept for the WASM process lifetime.
	// emscripten exposes only one "current" GL context at a time per
	// thread, so MakeCurrent before every render keeps us sticky against
	// any other (hypothetical) GL consumers in the same module.
	private static int s_glContextHandle;
	private static bool s_glReady;

	public static async Task<byte[]> RenderGaneshGlAsync (ISkiaScene scene, SKImageInfo info)
	{
		if (!s_glReady) {
			s_glContextHandle = await JsBridge.InitOffscreenGlAsync ();
			if (s_glContextHandle == 0)
				throw new InvalidOperationException (
					"globalThis.skiaSharpOffscreenGl.initAsync returned 0 — OffscreenCanvas/WebGL2 unavailable");
			s_glReady = true;
		}
		if (JsBridge.MakeGlCurrent (s_glContextHandle) == 0)
			throw new InvalidOperationException (
				$"globalThis.skiaSharpOffscreenGl.makeCurrent({s_glContextHandle}) failed");

		using var glInterface = GRGlInterface.Create ()
			?? throw new InvalidOperationException ("GRGlInterface.Create returned null");
		using var ctx = GRContext.CreateGl (glInterface)
			?? throw new InvalidOperationException ("GRContext.CreateGl returned null");
		using var surface = SKSurface.Create (ctx, budgeted: true, info)
			?? throw new InvalidOperationException ("SKSurface.Create returned null on Ganesh/WebGL2");

		scene.Draw (surface.Canvas);
		ctx.Flush ();
		// WebGL is synchronous: queued commands run on the GPU process and
		// glReadPixels blocks until they're done. No mapAsync dance needed
		// (unlike WebGPU); we can read pixels straight back from C#.
		ctx.Submit (synchronous: true);

		var rgba = new SKImageInfo (info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
		var pixels = new byte[rgba.BytesSize];
		unsafe {
			fixed (byte* p = pixels) {
				if (!surface.ReadPixels (rgba, (IntPtr)p, rgba.RowBytes, 0, 0))
					throw new InvalidOperationException ("SKSurface.ReadPixels failed on Ganesh/WebGL2");
			}
		}
		return pixels;
	}

	// Non-yielding mode disallows SKGraphiteContext.Dispose() while any GPU
	// work is in flight — that path asserts ("all GPU work must be finished
	// before destroying Context") and in WASM the assert becomes a fatal
	// `unreachable` trap. We mirror Uno's renderer pattern: bring up the
	// Context + Recorder once per session, leave them alive for the lifetime
	// of the WASM process, and only cycle the per-render Surface + backend
	// texture. Process-exit means the Skia warning never fires anyway.
	private static SKGraphiteDawnBackendContext s_bc;
	private static SKGraphiteContext s_ctx;
	private static SKGraphiteRecorder s_recorder;
	private static bool s_dawnReady;

	public static async Task<byte[]> RenderGraphiteDawnAsync (ISkiaScene scene, SKImageInfo info)
	{
		if (!s_dawnReady) {
			var handles = await JsBridge.InitWebGpuOffscreenAsync ()
				?? throw new InvalidOperationException (
					"globalThis.skiaSharpWebGpu.initOffscreenAsync returned null — no WebGPU available");
			s_bc = new SKGraphiteDawnBackendContext {
				WgpuInstance = (IntPtr)handles.GetPropertyAsInt32 ("instanceId"),
				WgpuDevice   = (IntPtr)handles.GetPropertyAsInt32 ("deviceId"),
				WgpuQueue    = (IntPtr)handles.GetPropertyAsInt32 ("queueId"),
				NonYielding  = true,
			};
			s_ctx = SKGraphiteContext.CreateDawn (s_bc)
				?? throw new InvalidOperationException ("SKGraphiteContext.CreateDawn returned null");
			s_recorder = s_ctx.CreateRecorder ()
				?? throw new InvalidOperationException ("CreateRecorder returned null");
			s_dawnReady = true;
		}

		// Create our own GPUTexture (via the bridge), wrap it as a Graphite
		// BackendTexture, render into it, then ask the bridge to read it
		// back asynchronously. We CAN'T use ctx.ReadPixels here: in
		// non-yielding mode it would deadlock on a mapAsync that needs the
		// JS event loop to tick. Bridge-side readback keeps the GPU→CPU
		// copy on the async path and never blocks a C# stack on JS work.
		var textureId = JsBridge.CreateOffscreenTexture (info.Width, info.Height);
		if (textureId == 0)
			throw new InvalidOperationException ("createOffscreenTexture returned 0");
		try {
			using var backendTex = SKGraphiteBackendTexture.CreateDawn ((IntPtr)textureId)
				?? throw new InvalidOperationException ("SKGraphiteBackendTexture.CreateDawn returned null");
			using var surface = SKSurface.Create (s_recorder, backendTex, SKColorType.Rgba8888)
				?? throw new InvalidOperationException ("SKSurface.Create returned null on Graphite/Dawn");

			scene.Draw (surface.Canvas);

			using (var recording = s_recorder.Snap ()
				?? throw new InvalidOperationException ("Recorder.Snap returned null")) {
				var status = s_ctx.InsertRecording (recording);
				if (status != SKGraphiteInsertStatus.Success)
					throw new InvalidOperationException ($"InsertRecording status = {status}");
			}
			s_ctx.Submit (new SKGraphiteSubmitInfo { Sync = SKGraphiteSyncToCpu.No });

			var b64 = await JsBridge.ReadTextureRgbaAsync (textureId, info.Width, info.Height)
				?? throw new InvalidOperationException ("readTextureRgbaAsync returned null");
			return Convert.FromBase64String (b64);
		} finally {
			JsBridge.ReleaseOffscreenTexture (textureId);
		}
	}

	private static byte[] ReadRgbaPremul (SKSurface surface, SKImageInfo info)
	{
		var rgba = new SKImageInfo (info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
		var pixels = new byte[rgba.BytesSize];
		unsafe {
			fixed (byte* p = pixels) {
				if (!surface.ReadPixels (rgba, (IntPtr)p, rgba.RowBytes, 0, 0))
					throw new InvalidOperationException ("SKSurface.ReadPixels failed");
			}
		}
		return pixels;
	}

	internal static partial class JsBridge
	{
		[JSImport ("globalThis.skiaSharpWebGpu.initOffscreenAsync")]
		internal static partial Task<JSObject?> InitWebGpuOffscreenAsync ();

		[JSImport ("globalThis.skiaSharpWebGpu.createOffscreenTexture")]
		internal static partial int CreateOffscreenTexture (int width, int height);

		[JSImport ("globalThis.skiaSharpWebGpu.readTextureRgbaAsync")]
		internal static partial Task<string?> ReadTextureRgbaAsync (int textureId, int width, int height);

		[JSImport ("globalThis.skiaSharpWebGpu.releaseOffscreenTexture")]
		internal static partial void ReleaseOffscreenTexture (int textureId);

		// Offscreen-WebGL2 bridge (skia_gl_bridge.js). InitOffscreenGl
		// returns an emscripten GL context handle; MakeGlCurrent binds it.
		[JSImport ("globalThis.skiaSharpOffscreenGl.initAsync")]
		internal static partial Task<int> InitOffscreenGlAsync ();

		[JSImport ("globalThis.skiaSharpOffscreenGl.makeCurrent")]
		internal static partial int MakeGlCurrent (int contextHandle);
	}
}
