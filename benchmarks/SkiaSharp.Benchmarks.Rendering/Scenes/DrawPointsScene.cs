using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// One DrawPoints call per SKPointMode (Points, Lines, Polygon) with 300 points
/// each. Exercises the point-primitive fast path — corresponds to Flutter's
/// <c>draw_points.dart</c>.
/// </summary>
public sealed class DrawPointsScene : ISkiaScene
{
	public string Name => "DrawPoints";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		var points = new SKPoint[300];
		for (var i = 0; i < points.Length; i++)
		{
			var x = (i * 11) % 240 + 8;
			var y = ((i * 17) + (i * i / 7)) % 240 + 8;
			points[i] = new SKPoint(x, y);
		}

		using var paint = new SKPaint
		{
			IsAntialias = true,
			StrokeWidth = 3,
			StrokeCap = SKStrokeCap.Round,
			Style = SKPaintStyle.Stroke,
		};

		paint.Color = SKColors.Crimson;
		canvas.DrawPoints(SKPointMode.Points, points, paint);

		paint.Color = SKColors.RoyalBlue.WithAlpha(120);
		paint.StrokeWidth = 1;
		canvas.DrawPoints(SKPointMode.Lines, points, paint);

		paint.Color = SKColors.SeaGreen.WithAlpha(120);
		canvas.DrawPoints(SKPointMode.Polygon, points, paint);
	}
}
