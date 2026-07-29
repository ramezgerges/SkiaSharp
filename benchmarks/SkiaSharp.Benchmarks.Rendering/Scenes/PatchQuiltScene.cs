using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A 12×8 grid of 96 Coons patches (bicubic surfaces with four-corner
/// gradient colours) via <see cref="SKCanvas.DrawPatch"/>. Each patch
/// is one call; the whole scene is 96 calls to <c>DrawPatch</c>.
///
/// <para>
/// Third leg of the "explicit-batch API" hypothesis stool alongside
/// <see cref="SpriteWallScene"/> (<c>DrawAtlas</c>, confirmed -28%) and
/// <see cref="VertexMeshScene"/> (<c>DrawVertices</c>, untested at scale).
/// Coons patches have their own dedicated pipeline in Graphite —
/// <c>BicubicPatchRenderStep</c> or similar — designed for gradient-mesh
/// rendering. Whether Graphite's win pattern extends here is genuinely
/// open: patches are complex per-primitive (tessellation on the fly)
/// but each call has a small self-contained mesh.
/// </para>
///
/// <para>
/// Patch corner points precomputed in the constructor. Each patch has
/// four Bézier edges (12 control points total) plus four corner colours
/// from a 24-hue palette rotated by cell index. Per-frame work is 96
/// DrawPatch calls; if Graphite batches them, this looks like a single
/// giant mesh draw; if it doesn't, this is 96 separate draws.
/// </para>
///
/// <para>
/// Deterministic. No clock, no RNG.
/// </para>
/// </summary>
public sealed class PatchQuiltScene : ISkiaScene
{
	public string Name => "PatchQuilt";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);

	private const int Cols = 12;
	private const int Rows = 8;
	private const int PatchCount = Cols * Rows; // 96

	private static readonly SKColor Background = new(0x1A, 0x1A, 0x1E);

	// 24-hue palette. Two colour indices per patch corner give visible
	// four-way gradients that look painterly.
	private static readonly SKColor[] Palette =
	{
		new(0xE5, 0x39, 0x35), new(0xEC, 0x40, 0x7A), new(0xAB, 0x47, 0xBC), new(0x7E, 0x57, 0xC2),
		new(0x5C, 0x6B, 0xC0), new(0x42, 0xA5, 0xF5), new(0x29, 0xB6, 0xF6), new(0x26, 0xC6, 0xDA),
		new(0x26, 0xA6, 0x9A), new(0x66, 0xBB, 0x6A), new(0x9C, 0xCC, 0x65), new(0xD4, 0xE1, 0x57),
		new(0xFF, 0xEE, 0x58), new(0xFF, 0xCA, 0x28), new(0xFF, 0xA7, 0x26), new(0xFF, 0x70, 0x43),
		new(0xE1, 0x50, 0x60), new(0x8D, 0x6E, 0x63), new(0x78, 0x90, 0x9C), new(0x60, 0x7D, 0x8B),
		new(0x54, 0x6E, 0x7A), new(0x00, 0xC8, 0x53), new(0x00, 0x83, 0x8F), new(0xBF, 0x36, 0x0C),
	};

	// Each patch needs 12 control points (4 edges × 3 points, with corners
	// shared between adjacent edges — see SkCanvas::drawPatch docs). We
	// precompute all 96 patches' control-point arrays in the constructor.
	private readonly SKPoint[][] cubicsPerPatch = new SKPoint[PatchCount][];
	private readonly SKColor[][] colorsPerPatch = new SKColor[PatchCount][];

	public PatchQuiltScene()
	{
		var patchW = 1024f / Cols;
		var patchH = 768f / Rows;
		for (var r = 0; r < Rows; r++)
		{
			for (var c = 0; c < Cols; c++)
			{
				var idx = r * Cols + c;
				var x0 = c * patchW;
				var y0 = r * patchH;
				var x1 = x0 + patchW;
				var y1 = y0 + patchH;

				// Perturb the two mid-edge control points on each edge so
				// the patch bulges outward slightly — makes the gradient
				// mesh look painterly rather than a flat trapezoid.
				var mx = patchW * 0.15f;
				var my = patchH * 0.15f;

				// 12 control points ordered clockwise starting from the
				// top-left corner. See SkCanvas.drawPatch docs.
				cubicsPerPatch[idx] = new SKPoint[]
				{
					new(x0, y0),                                  // 0: TL corner
					new(x0 + patchW / 3f, y0 - my),               // 1: top edge mid1 (bulge up)
					new(x0 + 2 * patchW / 3f, y0 + my),           // 2: top edge mid2 (bulge down)
					new(x1, y0),                                  // 3: TR corner
					new(x1 + mx, y0 + patchH / 3f),               // 4: right edge mid1
					new(x1 - mx, y0 + 2 * patchH / 3f),           // 5: right edge mid2
					new(x1, y1),                                  // 6: BR corner
					new(x1 - patchW / 3f, y1 + my),               // 7: bottom edge mid1
					new(x1 - 2 * patchW / 3f, y1 - my),           // 8: bottom edge mid2
					new(x0, y1),                                  // 9: BL corner
					new(x0 - mx, y1 - patchH / 3f),               // 10: left edge mid1
					new(x0 + mx, y1 - 2 * patchH / 3f),           // 11: left edge mid2
				};

				// Four corner colours. Deterministic pseudo-scatter across
				// the palette so adjacent patches share hues but not exactly.
				colorsPerPatch[idx] = new SKColor[]
				{
					Palette[(idx * 5 + 0) % Palette.Length],
					Palette[(idx * 5 + 7) % Palette.Length],
					Palette[(idx * 5 + 13) % Palette.Length],
					Palette[(idx * 5 + 19) % Palette.Length],
				};
			}
		}
	}

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(Background);
		// Same footgun as DrawVertices — default paint is opaque black and
		// SrcOver-of-black-over-vertex-colour = black. Use Modulate + white
		// paint so per-corner colours pass through.
		using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
		for (var i = 0; i < PatchCount; i++)
			canvas.DrawPatch(cubicsPerPatch[i], colorsPerPatch[i], texCoords: null, SKBlendMode.Modulate, paint);
	}
}
