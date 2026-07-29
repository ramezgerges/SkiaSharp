using SkiaSharp.Benchmarks.Rendering;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A heatmap-style grid of 2,016 small rounded rectangles (63×32) tinted
/// deterministically across a 24-colour palette. Every rrect is antialiased
/// and filled — no strokes, no shaders, no text, no filters. Pure batched
/// geometry with per-instance colour variation.
///
/// <para>
/// This targets Graphite's <c>AnalyticRoundRectStep</c> + <c>PaintParamsKey</c>
/// batching path. Graphite groups draws sharing the same pipeline more
/// aggressively than Ganesh — 2,000 rrects that differ only in colour
/// resolve to one pipeline + one uniform-buffer stream of per-instance
/// tints. The <see cref="SpriteWallScene"/> already showed Graphite ~28%
/// ahead on the equivalent pattern for textured quads (batched
/// <c>DrawAtlas</c>); this variant tests the same batching-shaped
/// architectural win for solid-fill rrects, which is what most real UIs
/// actually render.
/// </para>
///
/// <para>
/// Deterministic layout and colours — no clock, no RNG. Not partitioned
/// (each rrect is its own draw call, but the Skia backend batches them
/// internally; splitting the loop across worker Recorders would just
/// duplicate the batching cost N ways).
/// </para>
/// </summary>
public sealed class RRectGridScene : ISkiaScene
{
	public string Name => "RRectGrid";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);

	private const int Cols = 63;
	private const int Rows = 32;
	private const float MarginX = 8f;
	private const float MarginY = 8f;
	private const float GapX = 1.5f;
	private const float GapY = 1.5f;
	// Cell math is derived so the grid fills the surface with tight gaps.
	private const float CellW = (1024f - 2 * MarginX - (Cols - 1) * GapX) / Cols;      // ~15.6
	private const float CellH = (768f - 2 * MarginY - (Rows - 1) * GapY) / Rows;       // ~22.1
	private const float CornerR = 3.0f;

	private static readonly SKColor Background = new(0xF7, 0xF8, 0xFA);

	// 24-colour palette. Chosen so most tiles have decent contrast on white
	// and the per-tile hue pattern isn't just a spectrum sweep.
	private static readonly SKColor[] Palette =
	{
		new(0xE5, 0x39, 0x35), new(0xEC, 0x40, 0x7A), new(0xAB, 0x47, 0xBC), new(0x7E, 0x57, 0xC2),
		new(0x5C, 0x6B, 0xC0), new(0x42, 0xA5, 0xF5), new(0x29, 0xB6, 0xF6), new(0x26, 0xC6, 0xDA),
		new(0x26, 0xA6, 0x9A), new(0x66, 0xBB, 0x6A), new(0x9C, 0xCC, 0x65), new(0xD4, 0xE1, 0x57),
		new(0xFF, 0xEE, 0x58), new(0xFF, 0xCA, 0x28), new(0xFF, 0xA7, 0x26), new(0xFF, 0x70, 0x43),
		new(0x8D, 0x6E, 0x63), new(0x78, 0x90, 0x9C), new(0x60, 0x7D, 0x8B), new(0x54, 0x6E, 0x7A),
		new(0x37, 0x47, 0x4F), new(0x00, 0x83, 0x8F), new(0x00, 0x69, 0x7B), new(0xBF, 0x36, 0x0C),
	};

	// Precomputed per-cell tint indices — deterministic pseudo-scatter so
	// adjacent cells rarely share a colour without needing an RNG.
	private readonly int[] tintIndex;

	public RRectGridScene()
	{
		tintIndex = new int[Cols * Rows];
		for (var i = 0; i < tintIndex.Length; i++)
			tintIndex[i] = (i * 47 + (i / Cols) * 11) % Palette.Length;
	}

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(Background);

		// One reusable Paint that only mutates its Color between draws so
		// the backend can batch on pipeline identity. Antialiased so the
		// analytic-rrect path is exercised; if it were disabled we'd hit
		// the coverage-cast path instead.
		using var paint = new SKPaint { IsAntialias = true };

		for (var row = 0; row < Rows; row++)
		{
			var y = MarginY + row * (CellH + GapY);
			for (var col = 0; col < Cols; col++)
			{
				var x = MarginX + col * (CellW + GapX);
				paint.Color = Palette[tintIndex[row * Cols + col]];
				canvas.DrawRoundRect(x, y, CellW, CellH, CornerR, CornerR, paint);
			}
		}
	}
}
