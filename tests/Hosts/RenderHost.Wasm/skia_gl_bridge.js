// JS library merged into the consumer's final emcc link (via --js-library).
// Exposes a tiny offscreen-WebGL2 bridge so the wasm-ganesh-gles test cell
// can bring up a real WebGL2 context — registered with emscripten's $GL
// runtime so SkiaSharp's Ganesh-on-WebGL2 code path resolves its gl* shims
// against the right context — without touching DOM.
//
// Why a JS-library shim (and not plain JS): emscripten's $GL helper is a
// link-time JS object. It's only callable from JS that's been merged INTO
// the wasm bundle (mergeInto + __deps: ['$GL']). Plain page JS can't see it
// because dotnet.native.js encapsulates the Module inside an IIFE.

mergeInto(LibraryManager.library, {
    $SkiaSharpOffscreenGlBridge__deps: ['$GL'],
    $SkiaSharpOffscreenGlBridge__postset: 'SkiaSharpOffscreenGlBridge();',
    $SkiaSharpOffscreenGlBridge: function () {
        globalThis.skiaSharpOffscreenGl = {
            // Create an OffscreenCanvas + WebGL2 context, register it with
            // emscripten's $GL runtime, return the numeric context handle.
            // Caller invokes makeCurrent(handle) on every thread/await
            // boundary before issuing GL commands via SkiaSharp.
            //
            // The canvas size is irrelevant for offscreen testing — Skia's
            // SKSurface.Create(grContext, info) allocates its own FBO of
            // exactly info.Width × info.Height inside the GL context, and
            // the canvas's default framebuffer is never touched. 1×1 is
            // enough.
            initAsync: async function () {
                try {
                    if (typeof OffscreenCanvas === 'undefined') {
                        console.error('[skiaSharpOffscreenGl] OffscreenCanvas unavailable');
                        return 0;
                    }
                    var canvas = new OffscreenCanvas(1, 1);
                    var attrs = {
                        alpha: true, depth: false, stencil: true,
                        antialias: false, premultipliedAlpha: true,
                        preserveDrawingBuffer: false, powerPreference: 'low-power',
                    };
                    var ctx = canvas.getContext('webgl2', attrs);
                    if (!ctx) { console.error('[skiaSharpOffscreenGl] webgl2 context unavailable'); return 0; }

                    // GL.registerContext signature: (gl, attrs). attrs.majorVersion = 2
                    // tells emscripten's shim to wire up the WebGL2 entrypoints
                    // (glGetStringi, glDrawArraysInstanced, …) which Ganesh
                    // checks for to enable GLES3 features.
                    var handle = GL.registerContext(ctx, { majorVersion: 2 });
                    if (!handle) { console.error('[skiaSharpOffscreenGl] GL.registerContext returned 0'); return 0; }
                    return handle;
                } catch (e) {
                    console.error('[skiaSharpOffscreenGl] initAsync failed:', e);
                    return 0;
                }
            },

            // Bind the context to the current emscripten thread. Required
            // before each Skia call sequence — emscripten's gl* shims
            // dispatch through a thread-local "current" pointer.
            // Returns 1 on success, 0 on failure.
            makeCurrent: function (handle) {
                if (!handle) return 0;
                try {
                    return GL.makeContextCurrent(handle) ? 1 : 0;
                } catch (e) {
                    console.error('[skiaSharpOffscreenGl] makeContextCurrent failed:', e);
                    return 0;
                }
            },
        };
    },

    // Force-link anchor: a no-op exported C function so the library actually
    // ships (some emcc paths drop libraries with no referenced exports).
    sk_wasm_offscreen_gl_bridge_anchor__deps: ['$SkiaSharpOffscreenGlBridge'],
    sk_wasm_offscreen_gl_bridge_anchor__sig: 'v',
    sk_wasm_offscreen_gl_bridge_anchor: function () {},
});
