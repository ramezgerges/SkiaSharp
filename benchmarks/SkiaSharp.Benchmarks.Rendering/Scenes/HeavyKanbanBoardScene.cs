using SkiaSharp.Benchmarks.Rendering;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// KanbanBoard scaled up so per-partition work is well above thread-pool
/// dispatch latency. 12 partitions (3×4 cards), each rendering many more
/// tickets and drawing several gradient-filled path decorations — enough
/// work per partition (~1–2 ms sequential) that parallel dispatch can
/// actually pay off.
///
/// <para>
/// Deliberately does NOT use <see cref="SKMaskFilter.CreateBlur"/> for
/// shadows so the scene doesn't hit Graphite's known <c>RRectBlur</c>
/// regression. That regression is real but well-documented and unrelated
/// to the parallel-recording story we want to isolate here.
/// </para>
/// </summary>
public sealed class HeavyKanbanBoardScene : IPartitionedSkiaScene
{
	public string Name => "HeavyKanbanBoard";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);
	public int PartitionCount => 12;

	private static readonly SKColor[] TitleColors =
	{
		new(0x2A, 0x5F, 0xE6), new(0xC7, 0x25, 0x60), new(0x2B, 0x8B, 0x4B), new(0xB5, 0x67, 0x1F),
		new(0x7B, 0x35, 0xB8), new(0x0E, 0x87, 0x9A), new(0xC0, 0x39, 0x2B), new(0x4E, 0x60, 0x71),
	};

	private static readonly string[] Titles =
	{
		"Backlog", "Sprint", "In Progress", "Review",
		"Blocked", "QA", "Done", "Deploy",
		"Post-mortem", "Icebox", "Research", "Planned",
	};

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(new SKColor(0xF4, 0xF6, 0xF8));
		for (var i = 0; i < PartitionCount; i++)
			DrawPartition(canvas, i);
	}

	public void DrawPartition(SKCanvas canvas, int partitionIndex)
	{
		if (partitionIndex == 0)
			canvas.Clear(new SKColor(0xF4, 0xF6, 0xF8));

		var (col, row) = (partitionIndex % 4, partitionIndex / 4);
		var x = 20 + col * 250 + row * 6;   // slight row offset → overlap between rows
		var y = 20 + row * 250;

		DrawCard(canvas, x, y, partitionIndex);
	}

	private static void DrawCard(SKCanvas canvas, float x, float y, int idx)
	{
		const float cardW = 240f;
		const float cardH = 236f;
		var card = new SKRect(x, y, x + cardW, y + cardH);
		var titleBar = new SKRect(x, y, x + cardW, y + 46);

		// Card background — solid, no mask filter (avoids RRectBlur slow path).
		using (var bgPaint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawRoundRect(card, 12, 12, bgPaint);

		// Faux "elevation": a slightly-offset darker rounded rect underneath the card
		// (drawn BEFORE the card so it sits behind). Gives the shadow look without
		// SKMaskFilter.
		using (var shadowPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 30) })
		{
			var shadowRect = new SKRect(x + 3, y + 5, x + cardW + 3, y + cardH + 5);
			canvas.DrawRoundRect(shadowRect, 12, 12, shadowPaint);
		}
		// Redraw card on top of shadow (needed because we drew the card first for ordering above)
		using (var bgPaint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawRoundRect(card, 12, 12, bgPaint);

		// Title bar with a gradient fill
		using (var gradient = SKShader.CreateLinearGradient(
			new SKPoint(x, y), new SKPoint(x + cardW, y),
			new[] { TitleColors[idx % TitleColors.Length], TitleColors[(idx + 3) % TitleColors.Length] },
			null, SKShaderTileMode.Clamp))
		using (var titlePaint = new SKPaint { IsAntialias = true, Shader = gradient })
		using (var clip = new SKPath())
		{
			clip.AddRoundRect(card, 12, 12);
			canvas.Save();
			canvas.ClipPath(clip, antialias: true);
			canvas.DrawRect(titleBar, titlePaint);
			canvas.Restore();
		}

		using (var font = new SKFont { Size = 20, Embolden = true })
		using (var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawText(Titles[idx % Titles.Length], x + 14, y + 30, SKTextAlign.Left, font, textPaint);

		// Six tickets per card, more content than the light version.
		for (var t = 0; t < 6; t++)
		{
			var ty = y + 56 + t * 30;
			if (ty + 26 > y + cardH - 6) break;
			var ticket = new SKRect(x + 10, ty, x + cardW - 10, ty + 26);

			using (var tbg = new SKPaint { IsAntialias = true, Color = new SKColor(0xEC, 0xEE, 0xF1) })
				canvas.DrawRoundRect(ticket, 6, 6, tbg);

			// Priority badge — small filled circle
			using (var badgePaint = new SKPaint { IsAntialias = true, Color = TitleColors[(idx + t) % TitleColors.Length] })
				canvas.DrawCircle(x + 20, ty + 13, 5, badgePaint);

			// Ticket ID
			using (var font = new SKFont { Size = 11, Embolden = true })
			using (var textPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x33, 0x33, 0x33) })
				canvas.DrawText($"UI-{100 + idx * 11 + t}", x + 30, ty + 16, SKTextAlign.Left, font, textPaint);

			// Ticket title (fake, but at real UI font sizes so glyph work is real)
			using (var font = new SKFont { Size = 11 })
			using (var textPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x66, 0x66, 0x66) })
				canvas.DrawText($"Task {t + 1} for column {Titles[idx % Titles.Length]}", x + 80, ty + 16, SKTextAlign.Left, font, textPaint);

			// Progress bar — draws two rounded rects
			var barBg = new SKRect(x + 30, ty + 20, x + cardW - 10, ty + 24);
			using (var bg = new SKPaint { IsAntialias = true, Color = new SKColor(0xD1, 0xD5, 0xDB) })
				canvas.DrawRoundRect(barBg, 2, 2, bg);
			var progressWidth = ((t * 17 + idx * 23) % 100) / 100f * (cardW - 40 - 10);
			var barFg = new SKRect(x + 30, ty + 20, x + 30 + progressWidth, ty + 24);
			using (var fg = new SKPaint { IsAntialias = true, Color = TitleColors[(idx + t) % TitleColors.Length] })
				canvas.DrawRoundRect(barFg, 2, 2, fg);
		}
	}
}
