using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 48 arcs with varying sweep angles, alternating <c>useCenter</c>. Exercises
/// the arc rasterization fast path — corresponds to Flutter's <c>draw_arcs.dart</c>.
/// </summary>
public sealed class DrawArcsScene : ISkiaScene
{
	public string Name => "DrawArcs";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 3,
			StrokeCap = SKStrokeCap.Round,
		};
		using var fill = new SKPaint { IsAntialias = true };

		var palette = new[]
		{
			SKColors.Crimson, SKColors.RoyalBlue, SKColors.SeaGreen, SKColors.DarkOrange,
			SKColors.Purple, SKColors.Teal,
		};

		for (var i = 0; i < 48; i++)
		{
			var cx = ((i * 37) % 216) + 20;
			var cy = ((i * 53) % 216) + 20;
			var r = 12f + ((i % 5) * 4);
			var oval = new SKRect(cx - r, cy - r, cx + r, cy + r);
			var start = (i * 31) % 360;
			var sweep = 90f + ((i % 6) * 45);
			var useCenter = (i & 1) == 0;
			var paint = (i & 2) == 0 ? stroke : fill;
			paint.Color = palette[i % palette.Length].WithAlpha(180);
			canvas.DrawArc(oval, start, sweep, useCenter, paint);
		}
	}
}
