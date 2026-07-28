using SkiaSharp.Benchmarks.Rendering;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A card grid resembling a Kanban board — 8 cards laid out in a rough 4×2
/// grid, each with a rounded background, a coloured title bar, a body of
/// small shape/text content, and a soft drop-shadow. Cards intentionally
/// overlap by ~24 px so the composition is a genuinely non-trivial Z-order
/// stack (not a naive tile grid).
///
/// <para>
/// The scene implements <see cref="IPartitionedSkiaScene"/> with one
/// partition per card. Adjacent partitions overlap on the destination
/// surface; sequential backends draw them in index order and parallel
/// backends record them independently but insert in index order — both
/// produce identical output because destination pixel blending doesn't
/// care about the source thread.
/// </para>
/// </summary>
public sealed class KanbanBoardScene : IPartitionedSkiaScene
{
	public string Name => "KanbanBoard";
	public SKImageInfo Info => new(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
	public int PartitionCount => 8;

	private static readonly SKColor[] TitleColors =
	{
		new(0x2A, 0x5F, 0xE6), new(0xC7, 0x25, 0x60), new(0x2B, 0x8B, 0x4B), new(0xB5, 0x67, 0x1F),
		new(0x7B, 0x35, 0xB8), new(0x0E, 0x87, 0x9A), new(0xC0, 0x39, 0x2B), new(0x4E, 0x60, 0x71),
	};

	private static readonly string[] Titles =
	{
		"Backlog", "In Progress", "Review", "Blocked",
		"Done", "Deploy", "Post-mortem", "Icebox",
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
		{
			// Only partition 0 clears the background — subsequent partitions layer over.
			canvas.Clear(new SKColor(0xF4, 0xF6, 0xF8));
		}

		var (col, row) = (partitionIndex % 4, partitionIndex / 4);
		var x = 12 + col * 122 + (row * 8);   // slight offset per row → overlap
		var y = 12 + row * 224 + (col * 6);

		DrawCard(canvas, x, y, partitionIndex);
	}

	private static void DrawCard(SKCanvas canvas, float x, float y, int idx)
	{
		var cardW = 148f;
		var cardH = 232f;
		var card = new SKRect(x, y, x + cardW, y + cardH);
		var titleBar = new SKRect(x, y, x + cardW, y + 40);

		// Drop shadow
		using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4))
		using (var shadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black.WithAlpha(60), MaskFilter = blur })
		{
			var shadowRect = new SKRect(x + 2, y + 4, x + cardW + 2, y + cardH + 4);
			canvas.DrawRoundRect(shadowRect, 10, 10, shadowPaint);
		}

		// Card background
		using (var bgPaint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawRoundRect(card, 10, 10, bgPaint);

		// Title bar (coloured top strip)
		using (var titlePaint = new SKPaint { IsAntialias = true, Color = TitleColors[idx % TitleColors.Length] })
		using (var clip = new SKPath())
		{
			// Manually clip the title strip to the top-rounded portion of the card.
			clip.AddRoundRect(card, 10, 10);
			canvas.Save();
			canvas.ClipPath(clip, antialias: true);
			canvas.DrawRect(titleBar, titlePaint);
			canvas.Restore();
		}

		// Title text
		using (var font = new SKFont { Size = 18 })
		using (var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			canvas.DrawText(Titles[idx % Titles.Length], x + 12, y + 26, SKTextAlign.Left, font, textPaint);

		// Body: three "ticket" rows
		for (var t = 0; t < 3; t++)
		{
			var ty = y + 52 + t * 56;
			var ticket = new SKRect(x + 10, ty, x + cardW - 10, ty + 46);

			// Ticket bg
			using (var tbg = new SKPaint { IsAntialias = true, Color = new SKColor(0xEC, 0xEE, 0xF1) })
				canvas.DrawRoundRect(ticket, 6, 6, tbg);

			// Ticket badge (coloured pill)
			using (var badgePaint = new SKPaint { IsAntialias = true, Color = TitleColors[(idx + t) % TitleColors.Length].WithAlpha(200) })
			{
				var badge = new SKRect(x + 16, ty + 8, x + 44, ty + 22);
				canvas.DrawRoundRect(badge, 7, 7, badgePaint);
			}

			// Ticket text (fake)
			using (var font = new SKFont { Size = 11 })
			using (var textPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x33, 0x33, 0x33) })
			{
				canvas.DrawText($"UI-{100 + idx * 7 + t}", x + 50, ty + 20, SKTextAlign.Left, font, textPaint);
				canvas.DrawText($"Sub-task line {t + 1}", x + 16, ty + 36, SKTextAlign.Left, font, textPaint);
			}
		}
	}
}
