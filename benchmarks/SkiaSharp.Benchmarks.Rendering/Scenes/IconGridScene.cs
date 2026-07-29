using SkiaSharp.Benchmarks.Rendering;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 96 vector icons in an 8×12 grid, mimicking a design-system icon browser
/// (Material Icons, Fluent UI, SF Symbols). Each tile: colored circle
/// background, a 2-3 subpath vector icon glyph (stroked and filled with
/// distinct paints), a caption label underneath.
///
/// <para>
/// Designed to exercise the paint-state-change hot path in Skia's recording
/// backend — every icon has 3-4 unique paints (bg fill, icon stroke, icon
/// fill, label paint) so the frame goes through ~400 paint-cache lookups.
/// That's the pattern where parallel-recording should pay off, unlike
/// DashboardTiles' ~40 unique paints per frame.
/// </para>
///
/// <para>
/// Flat top-level structure — each tile draws at outer save depth, so
/// record→decompile→partition produces cleanly-balanced buckets. Direct
/// PartitionCount defaults to 8 (matches DashboardTiles' sweet spot); a
/// parallel-recording sweep should reveal at what N the win actually
/// materialises on state-heavy workloads.
/// </para>
/// </summary>
public sealed class IconGridScene : IPartitionedSkiaScene
{
	public string Name => "IconGrid";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);
	public int PartitionCount => 8;

	private const int Cols = 8;
	private const int Rows = 12;
	private const int TotalTiles = Cols * Rows; // 96
	private const float TileSize = 96f;
	private const float IconSize = 42f;
	private const float MarginX = 32f;
	private const float MarginY = 12f;
	private const float GapX = 32f;
	private const float GapY = 16f;

	private static readonly SKColor Background = new(0xFA, 0xFB, 0xFD);

	// Palette of 24 accents so different tiles get different colors. Chosen
	// to have good contrast on white and distinct enough that visual
	// inspection can spot rendering bugs.
	private static readonly SKColor[] Palette =
	{
		new(0xE5, 0x39, 0x35), new(0xEC, 0x40, 0x7A), new(0xAB, 0x47, 0xBC), new(0x7E, 0x57, 0xC2),
		new(0x5C, 0x6B, 0xC0), new(0x42, 0xA5, 0xF5), new(0x29, 0xB6, 0xF6), new(0x26, 0xC6, 0xDA),
		new(0x26, 0xA6, 0x9A), new(0x66, 0xBB, 0x6A), new(0x9C, 0xCC, 0x65), new(0xD4, 0xE1, 0x57),
		new(0xFF, 0xEE, 0x58), new(0xFF, 0xCA, 0x28), new(0xFF, 0xA7, 0x26), new(0xFF, 0x70, 0x43),
		new(0x8D, 0x6E, 0x63), new(0x78, 0x90, 0x9C), new(0x60, 0x7D, 0x8B), new(0x54, 0x6E, 0x7A),
		new(0x37, 0x47, 0x4F), new(0x00, 0x83, 0x8F), new(0x00, 0x69, 0x7B), new(0xBF, 0x36, 0x0C),
	};

	// 8 icon "families" — each a compact set of subpaths that a real icon
	// might have. Rotated across the 96 tiles so we hit different path
	// complexity, stroke vs fill balance, and different subpath counts.
	private enum Icon { Star, Heart, Envelope, Gear, Home, Bell, Chart, Cloud }

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(Background);
		for (var i = 0; i < PartitionCount; i++)
			DrawPartition(canvas, i);
	}

	public void DrawPartition(SKCanvas canvas, int partitionIndex)
	{
		if (partitionIndex == 0)
			canvas.Clear(Background);

		var start = partitionIndex * TotalTiles / PartitionCount;
		var end = (partitionIndex + 1) * TotalTiles / PartitionCount;
		for (var t = start; t < end; t++)
		{
			var col = t % Cols;
			var row = t / Cols;
			var x = MarginX + col * (TileSize + GapX);
			var y = MarginY + row * (TileSize + GapY);
			DrawIconTile(canvas, x, y, t);
		}
	}

	private static void DrawIconTile(SKCanvas canvas, float x, float y, int idx)
	{
		var accent = Palette[idx % Palette.Length];
		var icon = (Icon)(idx % 8);
		var cx = x + TileSize / 2f;
		var cy = y + TileSize / 2f - 8f;

		// Rounded background — a fresh SKPaint every tile so the state cache
		// gets exercised. In a real design-system browser these would come
		// from a theme provider and rebuild per frame under many popular
		// patterns (SwiftUI, Compose, React Native), so this is a fair model.
		using (var bg = new SKPaint { IsAntialias = true, Color = accent.WithAlpha(28) })
			canvas.DrawRoundRect(new SKRect(x, y, x + TileSize, y + TileSize), 12, 12, bg);

		using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = accent.WithAlpha(120) })
			canvas.DrawRoundRect(new SKRect(x + 0.75f, y + 0.75f, x + TileSize - 0.75f, y + TileSize - 0.75f), 12, 12, ring);

		// Icon glyph — 2-3 subpaths, mix of stroke and fill so each tile hits
		// paint-state changes several times.
		DrawIconGlyph(canvas, cx, cy, IconSize, accent, icon, idx);

		// Caption underneath.
		using (var font = new SKFont { Size = 9 })
		using (var text = new SKPaint { IsAntialias = true, Color = new SKColor(0x33, 0x33, 0x33) })
			canvas.DrawText(IconName(icon), cx, y + TileSize - 4, SKTextAlign.Center, font, text);
	}

	private static void DrawIconGlyph(SKCanvas canvas, float cx, float cy, float size, SKColor accent, Icon icon, int idx)
	{
		var r = size / 2f;
		switch (icon)
		{
			case Icon.Star:
			{
				using var p = new SKPath();
				for (var i = 0; i < 10; i++)
				{
					var angle = -System.MathF.PI / 2 + i * System.MathF.PI / 5;
					var rr = (i % 2 == 0) ? r : r * 0.45f;
					var px = cx + rr * System.MathF.Cos(angle);
					var py = cy + rr * System.MathF.Sin(angle);
					if (i == 0) p.MoveTo(px, py); else p.LineTo(px, py);
				}
				p.Close();
				using (var fill = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(p, fill);
				using (var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = accent.WithAlpha(220), StrokeJoin = SKStrokeJoin.Round })
					canvas.DrawPath(p, stroke);
				break;
			}
			case Icon.Heart:
			{
				using var p = new SKPath();
				p.MoveTo(cx, cy + r * 0.9f);
				p.CubicTo(cx - r * 1.4f, cy + r * 0.1f, cx - r * 0.7f, cy - r * 0.9f, cx, cy - r * 0.15f);
				p.CubicTo(cx + r * 0.7f, cy - r * 0.9f, cx + r * 1.4f, cy + r * 0.1f, cx, cy + r * 0.9f);
				p.Close();
				using (var fill = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(p, fill);
				using (var glow = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(180) })
					canvas.DrawCircle(cx - r * 0.35f, cy - r * 0.35f, r * 0.15f, glow);
				break;
			}
			case Icon.Envelope:
			{
				var w = size * 1.1f; var h = size * 0.7f;
				var body = new SKRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
				using (var fill = new SKPaint { IsAntialias = true, Color = accent.WithAlpha(220) }) canvas.DrawRect(body, fill);
				using var flap = new SKPath();
				flap.MoveTo(body.Left, body.Top);
				flap.LineTo(cx, cy);
				flap.LineTo(body.Right, body.Top);
				flap.Close();
				using (var fillFlap = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(flap, fillFlap);
				using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = SKColors.White.WithAlpha(200) })
					canvas.DrawRect(body, edge);
				break;
			}
			case Icon.Gear:
			{
				const int teeth = 8;
				using var p = new SKPath();
				for (var i = 0; i < teeth * 2; i++)
				{
					var a = i * System.MathF.PI / teeth;
					var rr = (i % 2 == 0) ? r : r * 0.72f;
					var px = cx + rr * System.MathF.Cos(a);
					var py = cy + rr * System.MathF.Sin(a);
					if (i == 0) p.MoveTo(px, py); else p.LineTo(px, py);
				}
				p.Close();
				using (var fill = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(p, fill);
				using (var hole = new SKPaint { IsAntialias = true, Color = SKColors.White }) canvas.DrawCircle(cx, cy, r * 0.3f, hole);
				using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = accent.WithAlpha(210) })
					canvas.DrawCircle(cx, cy, r * 0.3f, edge);
				break;
			}
			case Icon.Home:
			{
				using var p = new SKPath();
				p.MoveTo(cx - r, cy);
				p.LineTo(cx, cy - r);
				p.LineTo(cx + r, cy);
				p.LineTo(cx + r * 0.75f, cy);
				p.LineTo(cx + r * 0.75f, cy + r * 0.85f);
				p.LineTo(cx - r * 0.75f, cy + r * 0.85f);
				p.LineTo(cx - r * 0.75f, cy);
				p.Close();
				using (var fill = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(p, fill);
				var door = new SKRect(cx - r * 0.22f, cy + r * 0.2f, cx + r * 0.22f, cy + r * 0.85f);
				using (var doorFill = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(220) }) canvas.DrawRect(door, doorFill);
				break;
			}
			case Icon.Bell:
			{
				using var p = new SKPath();
				p.MoveTo(cx - r * 0.85f, cy + r * 0.35f);
				p.CubicTo(cx - r * 0.85f, cy - r * 0.55f, cx - r * 0.55f, cy - r, cx, cy - r);
				p.CubicTo(cx + r * 0.55f, cy - r, cx + r * 0.85f, cy - r * 0.55f, cx + r * 0.85f, cy + r * 0.35f);
				p.LineTo(cx + r * 1.05f, cy + r * 0.55f);
				p.LineTo(cx - r * 1.05f, cy + r * 0.55f);
				p.Close();
				using (var fill = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(p, fill);
				using (var clapper = new SKPaint { IsAntialias = true, Color = accent.WithAlpha(230) })
					canvas.DrawCircle(cx, cy + r * 0.8f, r * 0.2f, clapper);
				break;
			}
			case Icon.Chart:
			{
				var bars = new[] { 0.35f, 0.72f, 0.5f, 0.9f, 0.6f };
				var bw = size * 0.14f;
				var baseY = cy + r * 0.9f;
				using var barPaint = new SKPaint { IsAntialias = true, Color = accent };
				for (var b = 0; b < bars.Length; b++)
				{
					var bx = cx - size / 2 + b * (bw + 3);
					var bh = size * bars[b];
					canvas.DrawRect(new SKRect(bx, baseY - bh, bx + bw, baseY), barPaint);
				}
				using var axis = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = new SKColor(0x33, 0x33, 0x33) };
				canvas.DrawLine(cx - r, baseY, cx + r, baseY, axis);
				break;
			}
			case Icon.Cloud:
			{
				using var p = new SKPath();
				p.MoveTo(cx - r, cy + r * 0.4f);
				p.CubicTo(cx - r * 1.4f, cy + r * 0.4f, cx - r * 1.4f, cy - r * 0.2f, cx - r * 0.7f, cy - r * 0.25f);
				p.CubicTo(cx - r * 0.7f, cy - r * 0.9f, cx + r * 0.3f, cy - r * 0.9f, cx + r * 0.35f, cy - r * 0.25f);
				p.CubicTo(cx + r * 1.1f, cy - r * 0.4f, cx + r * 1.1f, cy + r * 0.45f, cx + r * 0.5f, cy + r * 0.4f);
				p.Close();
				using (var fill = new SKPaint { IsAntialias = true, Color = accent }) canvas.DrawPath(p, fill);
				using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = SKColors.White.WithAlpha(180) })
					canvas.DrawPath(p, edge);
				break;
			}
		}
	}

	private static string IconName(Icon icon) => icon switch
	{
		Icon.Star => "star",
		Icon.Heart => "heart",
		Icon.Envelope => "mail",
		Icon.Gear => "settings",
		Icon.Home => "home",
		Icon.Bell => "notify",
		Icon.Chart => "chart",
		Icon.Cloud => "cloud",
		_ => "?",
	};
}
