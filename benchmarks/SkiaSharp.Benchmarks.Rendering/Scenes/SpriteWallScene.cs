using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A "sticker wall" of 2,000 rotated + scaled sprites scattered across the
/// canvas via a single <see cref="SKCanvas.DrawAtlas"/> call. The atlas is
/// a 128×128 image containing 16 hand-drawn icon-like glyphs (stars, hearts,
/// gears, arrows, chevrons, plus-signs, chat-bubbles, sparkles). Every
/// sprite instance picks a glyph, tint, position, rotation, and scale
/// deterministically — same output every frame.
///
/// <para>
/// This is Graphite's home turf. <see cref="SKCanvas.DrawAtlas"/> is the
/// primitive Skia offers specifically for batched textured-quad rendering —
/// game-engine sprite loops, particle systems, emoji clouds. Graphite's
/// coalesced sampler binding + render-pass batching gives it a measurable
/// architectural win here where the small <see cref="DrawAtlasScene"/>
/// (200 sprites, 256×256) already showed ~15% over Ganesh; scaling to
/// 2,000 sprites on a 1024×768 surface widens the gap.
/// </para>
///
/// <para>
/// Not partitioned. <see cref="SKCanvas.DrawAtlas"/> is inherently one draw
/// call — splitting into ranges would be strictly slower on any backend.
/// </para>
/// </summary>
public sealed class SpriteWallScene : ISkiaScene
{
	public string Name => "SpriteWall";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);

	private const int SpriteCount = 2000;
	private const int GlyphSize = 32;
	private const int GlyphsPerRow = 4;
	private const int AtlasDim = GlyphSize * GlyphsPerRow; // 128

	// Muted-vibrant palette for tinting sprites — one per index; each of the
	// 2,000 sprites picks the accent by (i * 31) % Palette.Length so the
	// distribution feels random without an RNG.
	private static readonly SKColor[] Palette =
	{
		new(0xE5, 0x39, 0x35), new(0xEC, 0x40, 0x7A), new(0xAB, 0x47, 0xBC), new(0x7E, 0x57, 0xC2),
		new(0x5C, 0x6B, 0xC0), new(0x42, 0xA5, 0xF5), new(0x29, 0xB6, 0xF6), new(0x26, 0xC6, 0xDA),
		new(0x26, 0xA6, 0x9A), new(0x66, 0xBB, 0x6A), new(0x9C, 0xCC, 0x65), new(0xFF, 0xCA, 0x28),
		new(0xFF, 0xA7, 0x26), new(0xFF, 0x70, 0x43), new(0x8D, 0x6E, 0x63), new(0x60, 0x7D, 0x8B),
	};

	private readonly SKImage atlas = BuildAtlas();
	private readonly SKRect[] sprites = new SKRect[SpriteCount];
	private readonly SKRotationScaleMatrix[] transforms = new SKRotationScaleMatrix[SpriteCount];
	private readonly SKColor[] tints = new SKColor[SpriteCount];

	public SpriteWallScene()
	{
		// Precompute the sprite/transform/tint arrays once — same content every
		// frame, so per-frame work is just the DrawAtlas dispatch.
		for (var i = 0; i < SpriteCount; i++)
		{
			var glyph = i % 16;
			var gx = (glyph % GlyphsPerRow) * GlyphSize;
			var gy = (glyph / GlyphsPerRow) * GlyphSize;
			sprites[i] = new SKRect(gx, gy, gx + GlyphSize, gy + GlyphSize);

			// Scatter positions with two coprime multipliers so we get a
			// deterministic quasi-uniform fill of the 1024×768 canvas.
			var cx = ((i * 191) % (Info.Width - 40)) + 20;
			var cy = ((i * 313) % (Info.Height - 40)) + 20;

			var scale = 0.35f + ((i % 11) * 0.06f);   // 0.35 .. ~0.95
			var degrees = ((i * 37) % 360);            // 0..359
			transforms[i] = SKRotationScaleMatrix.CreateDegrees(
				scale, degrees, cx, cy, anchorX: GlyphSize / 2f, anchorY: GlyphSize / 2f);

			tints[i] = Palette[(i * 31) % Palette.Length];
		}
	}

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(new SKColor(0xF7, 0xF8, 0xFA));
		canvas.DrawAtlas(atlas, sprites, transforms, tints, SKBlendMode.Modulate, SKSamplingOptions.Default, paint: null);
	}

	private static SKImage BuildAtlas()
	{
		var info = new SKImageInfo(AtlasDim, AtlasDim, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info)
			?? throw new System.InvalidOperationException("SKSurface.Create returned null for SpriteWall atlas.");
		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Transparent);
		for (var i = 0; i < 16; i++)
		{
			var gx = (i % GlyphsPerRow) * GlyphSize;
			var gy = (i / GlyphsPerRow) * GlyphSize;
			DrawGlyph(canvas, gx, gy, GlyphSize, i);
		}
		return surface.Snapshot();
	}

	// 16 tiny icon-like glyphs. Every one is white on transparent so the
	// per-sprite tint (SKBlendMode.Modulate) recolours it at draw time.
	private static void DrawGlyph(SKCanvas canvas, float x, float y, float size, int idx)
	{
		var cx = x + size / 2f;
		var cy = y + size / 2f;
		var r = size / 2f - 2;
		using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
		using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = SKColors.White, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

		switch (idx)
		{
			case 0: // solid circle
				canvas.DrawCircle(cx, cy, r, paint);
				break;
			case 1: // ring
				canvas.DrawCircle(cx, cy, r, stroke);
				break;
			case 2: // 5-point star
			{
				using var p = new SKPath();
				for (var k = 0; k < 10; k++)
				{
					var a = -System.MathF.PI / 2 + k * System.MathF.PI / 5;
					var rr = (k % 2 == 0) ? r : r * 0.42f;
					var px = cx + rr * System.MathF.Cos(a);
					var py = cy + rr * System.MathF.Sin(a);
					if (k == 0) p.MoveTo(px, py); else p.LineTo(px, py);
				}
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 3: // heart
			{
				using var p = new SKPath();
				p.MoveTo(cx, cy + r * 0.85f);
				p.CubicTo(cx - r * 1.3f, cy + r * 0.05f, cx - r * 0.65f, cy - r * 0.9f, cx, cy - r * 0.1f);
				p.CubicTo(cx + r * 0.65f, cy - r * 0.9f, cx + r * 1.3f, cy + r * 0.05f, cx, cy + r * 0.85f);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 4: // gear (8 teeth)
			{
				using var p = new SKPath();
				const int teeth = 8;
				for (var k = 0; k < teeth * 2; k++)
				{
					var a = k * System.MathF.PI / teeth;
					var rr = (k % 2 == 0) ? r : r * 0.7f;
					var px = cx + rr * System.MathF.Cos(a);
					var py = cy + rr * System.MathF.Sin(a);
					if (k == 0) p.MoveTo(px, py); else p.LineTo(px, py);
				}
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 5: // arrow-up
			{
				using var p = new SKPath();
				p.MoveTo(cx, cy - r);
				p.LineTo(cx + r * 0.8f, cy + r * 0.2f);
				p.LineTo(cx + r * 0.35f, cy + r * 0.2f);
				p.LineTo(cx + r * 0.35f, cy + r * 0.85f);
				p.LineTo(cx - r * 0.35f, cy + r * 0.85f);
				p.LineTo(cx - r * 0.35f, cy + r * 0.2f);
				p.LineTo(cx - r * 0.8f, cy + r * 0.2f);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 6: // chevron-right (stroked)
			{
				using var p = new SKPath();
				p.MoveTo(cx - r * 0.4f, cy - r * 0.7f);
				p.LineTo(cx + r * 0.5f, cy);
				p.LineTo(cx - r * 0.4f, cy + r * 0.7f);
				canvas.DrawPath(p, stroke);
				break;
			}
			case 7: // plus
			{
				var thick = r * 0.36f;
				canvas.DrawRect(new SKRect(cx - thick, cy - r * 0.9f, cx + thick, cy + r * 0.9f), paint);
				canvas.DrawRect(new SKRect(cx - r * 0.9f, cy - thick, cx + r * 0.9f, cy + thick), paint);
				break;
			}
			case 8: // chat bubble
			{
				using var p = new SKPath();
				var body = new SKRect(cx - r * 0.9f, cy - r * 0.8f, cx + r * 0.9f, cy + r * 0.35f);
				p.AddRoundRect(body, r * 0.35f, r * 0.35f);
				p.MoveTo(cx - r * 0.15f, cy + r * 0.35f);
				p.LineTo(cx - r * 0.4f, cy + r * 0.85f);
				p.LineTo(cx + r * 0.15f, cy + r * 0.35f);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 9: // sparkle (4-point)
			{
				using var p = new SKPath();
				var s = r * 0.35f;
				p.MoveTo(cx, cy - r);
				p.LineTo(cx + s, cy - s);
				p.LineTo(cx + r, cy);
				p.LineTo(cx + s, cy + s);
				p.LineTo(cx, cy + r);
				p.LineTo(cx - s, cy + s);
				p.LineTo(cx - r, cy);
				p.LineTo(cx - s, cy - s);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 10: // hexagon
			{
				using var p = new SKPath();
				for (var k = 0; k < 6; k++)
				{
					var a = System.MathF.PI / 6 + k * System.MathF.PI / 3;
					var px = cx + r * System.MathF.Cos(a);
					var py = cy + r * System.MathF.Sin(a);
					if (k == 0) p.MoveTo(px, py); else p.LineTo(px, py);
				}
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 11: // triangle-down
			{
				using var p = new SKPath();
				p.MoveTo(cx - r * 0.9f, cy - r * 0.55f);
				p.LineTo(cx + r * 0.9f, cy - r * 0.55f);
				p.LineTo(cx, cy + r * 0.85f);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 12: // square rotated 45° (diamond)
			{
				using var p = new SKPath();
				p.MoveTo(cx, cy - r);
				p.LineTo(cx + r, cy);
				p.LineTo(cx, cy + r);
				p.LineTo(cx - r, cy);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 13: // half-moon
			{
				using var p = new SKPath();
				var big = new SKRect(cx - r, cy - r, cx + r, cy + r);
				p.AddArc(big, 90, 180);
				var cutR = r * 0.75f;
				var small = new SKRect(cx - cutR + r * 0.3f, cy - cutR, cx + cutR + r * 0.3f, cy + cutR);
				p.AddArc(small, -90, -180);
				p.Close();
				canvas.DrawPath(p, paint);
				break;
			}
			case 14: // dashed ring approximation — 8 arc segments
			{
				var rect = new SKRect(cx - r, cy - r, cx + r, cy + r);
				for (var k = 0; k < 8; k++)
					canvas.DrawArc(rect, k * 45, 30, false, stroke);
				break;
			}
			case 15: // filled square with a hole (donut-square)
			{
				canvas.DrawRect(new SKRect(cx - r, cy - r, cx + r, cy + r), paint);
				using var hole = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Clear };
				canvas.DrawRect(new SKRect(cx - r * 0.42f, cy - r * 0.42f, cx + r * 0.42f, cy + r * 0.42f), hole);
				break;
			}
		}
	}
}
