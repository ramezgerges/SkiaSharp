using SkiaSharp.Benchmarks.Rendering;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A heterogeneous 5×4 tile dashboard — 20 tiles across 8 different widget
/// types (KPI number, bar chart, sparkline, stat list, gauge, progress
/// ring, mini calendar, activity feed). Roughly resembles a real
/// admin/analytics UI: text-heavy, gradient headers, rounded panels,
/// small vector shapes.
///
/// <para>
/// Unlike <see cref="KanbanBoardScene"/> (uniform cards) or
/// <see cref="HighDrawCountScene"/> (5000 flat rects), this scene
/// intentionally mixes tile types so per-partition work varies a bit,
/// exercising thread-pool scheduling more realistically. Every tile is
/// drawn at the outer save depth, so the auto-partitioner sees a clean
/// depth-0 boundary between each one — record → decompile-partition
/// produces balanced buckets.
/// </para>
/// </summary>
public sealed class DashboardTilesScene : IPartitionedSkiaScene
{
	public string Name => "DashboardTiles";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);
	public int PartitionCount => 20;

	private const int Cols = 5;
	private const int Rows = 4;
	private const float TileW = 190f;
	private const float TileH = 172f;
	private const float MarginX = 22f;
	private const float MarginY = 20f;
	private const float GapX = 12f;
	private const float GapY = 14f;

	private static readonly SKColor Background = new(0xF3, 0xF5, 0xF9);

	private static readonly SKColor[] Accents =
	{
		new(0x2A, 0x5F, 0xE6), new(0x0E, 0x87, 0x9A), new(0x2B, 0x8B, 0x4B), new(0xB5, 0x67, 0x1F),
		new(0x7B, 0x35, 0xB8), new(0xC7, 0x25, 0x60), new(0xC0, 0x39, 0x2B), new(0x4E, 0x60, 0x71),
	};

	private static readonly string[] Labels =
	{
		"Revenue", "Users", "Sessions", "Bounce",
		"Latency", "Errors", "CPU", "Memory",
		"Deploys", "Uptime", "Reviews", "Alerts",
		"Signups", "Trials", "Churn", "Retention",
		"Storage", "Requests", "Cache", "Queue",
	};

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

		var col = partitionIndex % Cols;
		var row = partitionIndex / Cols;
		var x = MarginX + col * (TileW + GapX);
		var y = MarginY + row * (TileH + GapY);

		DrawTile(canvas, x, y, partitionIndex);
	}

	private static void DrawTile(SKCanvas canvas, float x, float y, int idx)
	{
		var tile = new SKRect(x, y, x + TileW, y + TileH);
		var accent = Accents[idx % Accents.Length];
		var label = Labels[idx % Labels.Length];

		// Faux elevation shadow (a slightly offset darker rounded rect) — no MaskFilter,
		// so we don't hit Graphite's RRectBlur slow path.
		using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 24) })
			canvas.DrawRoundRect(new SKRect(x + 2, y + 4, x + TileW + 2, y + TileH + 4), 10, 10, shadow);

		// Tile background.
		using (var bg = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawRoundRect(tile, 10, 10, bg);

		// Header strip with a subtle gradient — clipped to the top-rounded portion.
		var header = new SKRect(x, y, x + TileW, y + 36);
		using (var clip = new SKPath())
		{
			clip.AddRoundRect(tile, 10, 10);
			canvas.Save();
			canvas.ClipPath(clip, antialias: true);
			using (var gradient = SKShader.CreateLinearGradient(
				new SKPoint(x, y), new SKPoint(x + TileW, y),
				new[] { accent, Accents[(idx + 2) % Accents.Length] },
				null, SKShaderTileMode.Clamp))
			using (var paint = new SKPaint { IsAntialias = true, Shader = gradient })
				canvas.DrawRect(header, paint);
			canvas.Restore();
		}

		// Header label.
		using (var font = new SKFont { Size = 13, Embolden = true })
		using (var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawText(label, x + 12, y + 23, SKTextAlign.Left, font, textPaint);

		// Body — pick one of 8 widget types by tile index.
		var bodyTop = y + 44;
		var bodyH = TileH - 44 - 8;
		var body = new SKRect(x + 8, bodyTop, x + TileW - 8, bodyTop + bodyH);
		switch (idx % 8)
		{
			case 0: DrawKpiBody(canvas, body, idx, accent); break;
			case 1: DrawBarChartBody(canvas, body, idx, accent); break;
			case 2: DrawSparklineBody(canvas, body, idx, accent); break;
			case 3: DrawStatListBody(canvas, body, idx, accent); break;
			case 4: DrawGaugeBody(canvas, body, idx, accent); break;
			case 5: DrawProgressRingBody(canvas, body, idx, accent); break;
			case 6: DrawMiniCalendarBody(canvas, body, idx, accent); break;
			case 7: DrawActivityFeedBody(canvas, body, idx, accent); break;
		}
	}

	private static void DrawKpiBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		var value = (idx * 137 + 421) % 10_000;
		var delta = ((idx * 17) % 20) - 10;
		using (var font = new SKFont { Size = 34, Embolden = true })
		using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x22, 0x22, 0x22) })
			canvas.DrawText($"{value:N0}", body.Left + 6, body.Top + 38, SKTextAlign.Left, font, paint);

		using (var font = new SKFont { Size = 11 })
		using (var paint = new SKPaint { IsAntialias = true, Color = delta >= 0 ? Accents[2] : Accents[6] })
			canvas.DrawText($"{(delta >= 0 ? "▲" : "▼")} {System.Math.Abs(delta)}%", body.Left + 6, body.Top + 58, SKTextAlign.Left, font, paint);

		using (var font = new SKFont { Size = 10 })
		using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x88, 0x88, 0x88) })
			canvas.DrawText("vs. last week", body.Left + 6, body.Top + 78, SKTextAlign.Left, font, paint);
	}

	private static void DrawBarChartBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		const int bars = 7;
		var barW = (body.Width - 20) / bars - 4;
		for (var b = 0; b < bars; b++)
		{
			var h = ((idx * 13 + b * b * 7) % 80) + 10;
			var bx = body.Left + 10 + b * (barW + 4);
			var rect = new SKRect(bx, body.Bottom - h - 6, bx + barW, body.Bottom - 6);
			using var paint = new SKPaint
			{
				IsAntialias = true,
				Color = b == (idx * 3 % bars) ? accent : accent.WithAlpha(140),
			};
			canvas.DrawRoundRect(rect, 2, 2, paint);
		}
	}

	private static void DrawSparklineBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		const int pts = 24;
		using var path = new SKPath();
		var midY = body.Top + body.Height / 2;
		for (var p = 0; p < pts; p++)
		{
			var px = body.Left + 8 + (body.Width - 16) * p / (pts - 1f);
			var py = midY + (float)System.Math.Sin((idx * 0.6f) + p * 0.5f) * (body.Height / 3f)
				+ ((p * idx * 7) % 13) - 6;
			if (p == 0) path.MoveTo(px, py); else path.LineTo(px, py);
		}
		using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = accent })
			canvas.DrawPath(path, paint);

		// End marker
		var lastP = path.LastPoint;
		using (var dot = new SKPaint { IsAntialias = true, Color = accent })
			canvas.DrawCircle(lastP, 3.5f, dot);
	}

	private static void DrawStatListBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		string[] labels = { "P50", "P95", "P99", "Max" };
		using var font = new SKFont { Size = 11 };
		using var valFont = new SKFont { Size = 11, Embolden = true };
		using var labelPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x66, 0x66, 0x66) };
		using var valPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x22, 0x22, 0x22) };
		for (var r = 0; r < labels.Length; r++)
		{
			var ry = body.Top + 14 + r * 22;
			canvas.DrawText(labels[r], body.Left + 6, ry, SKTextAlign.Left, font, labelPaint);
			var v = ((idx * 31 + r * 13) % 500) + 20;
			canvas.DrawText($"{v} ms", body.Right - 6, ry, SKTextAlign.Right, valFont, valPaint);
		}
	}

	private static void DrawGaugeBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		var cx = body.MidX;
		var cy = body.Bottom - 12;
		var r = System.Math.Min(body.Width / 2 - 8, body.Height - 20);
		var arcRect = new SKRect(cx - r, cy - r, cx + r, cy + r);

		using (var bg = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, Color = new SKColor(0xE1, 0xE5, 0xEB) })
			canvas.DrawArc(arcRect, 180, 180, false, bg);

		var pct = ((idx * 17 + 43) % 90) + 5;
		using (var fg = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, Color = accent, StrokeCap = SKStrokeCap.Round })
			canvas.DrawArc(arcRect, 180, 180 * pct / 100f, false, fg);

		using (var font = new SKFont { Size = 20, Embolden = true })
		using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x22, 0x22, 0x22) })
			canvas.DrawText($"{pct}%", cx, cy - 4, SKTextAlign.Center, font, paint);
	}

	private static void DrawProgressRingBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		var cx = body.MidX;
		var cy = body.MidY;
		var r = System.Math.Min(body.Width / 2 - 10, body.Height / 2 - 6);
		var ringRect = new SKRect(cx - r, cy - r, cx + r, cy + r);

		using (var bg = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, Color = new SKColor(0xE8, 0xEB, 0xF0) })
			canvas.DrawArc(ringRect, 0, 360, false, bg);

		var pct = ((idx * 23 + 17) % 85) + 10;
		using (var fg = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, Color = accent, StrokeCap = SKStrokeCap.Round })
			canvas.DrawArc(ringRect, -90, 360 * pct / 100f, false, fg);

		using (var font = new SKFont { Size = 18, Embolden = true })
		using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x22, 0x22, 0x22) })
			canvas.DrawText($"{pct}%", cx, cy + 6, SKTextAlign.Center, font, paint);
	}

	private static void DrawMiniCalendarBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		const int cols = 7;
		const int rows = 5;
		var cellW = (body.Width - 8) / cols;
		var cellH = (body.Height - 8) / rows;
		var today = ((idx * 11) % 28) + 1;
		using var font = new SKFont { Size = 9 };
		using var dayPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x66, 0x66, 0x66) };
		using var todayFillPaint = new SKPaint { IsAntialias = true, Color = accent };
		using var todayTextPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
		for (var r = 0; r < rows; r++)
		{
			for (var c = 0; c < cols; c++)
			{
				var day = r * cols + c + 1;
				if (day > 31) break;
				var cx = body.Left + 4 + c * cellW + cellW / 2;
				var cy = body.Top + 4 + r * cellH + cellH / 2 + 3;
				if (day == today)
				{
					canvas.DrawCircle(cx, cy - 3, System.Math.Min(cellW, cellH) / 2 - 2, todayFillPaint);
					canvas.DrawText($"{day}", cx, cy, SKTextAlign.Center, font, todayTextPaint);
				}
				else
				{
					canvas.DrawText($"{day}", cx, cy, SKTextAlign.Center, font, dayPaint);
				}
			}
		}
	}

	private static void DrawActivityFeedBody(SKCanvas canvas, SKRect body, int idx, SKColor accent)
	{
		string[] verbs = { "pushed to", "opened", "reviewed", "closed", "merged" };
		using var nameFont = new SKFont { Size = 11, Embolden = true };
		using var descFont = new SKFont { Size = 10 };
		using var namePaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x22, 0x22, 0x22) };
		using var descPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x66, 0x66, 0x66) };
		using var dotPaint = new SKPaint { IsAntialias = true, Color = accent };
		for (var r = 0; r < 4; r++)
		{
			var ry = body.Top + 12 + r * 22;
			canvas.DrawCircle(body.Left + 10, ry - 3, 4, dotPaint);
			var author = (char)('A' + (idx + r) % 26);
			canvas.DrawText($"{author}. Smith", body.Left + 22, ry, SKTextAlign.Left, nameFont, namePaint);
			var verb = verbs[(idx + r) % verbs.Length];
			canvas.DrawText($"{verb} #{100 + idx * 7 + r}", body.Left + 22, ry + 13, SKTextAlign.Left, descFont, descPaint);
		}
	}
}
