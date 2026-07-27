using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A dense field of stroked cubic bezier curves — exercises path tessellation,
/// which is where Ganesh's atlased fills and Graphite's tessellator take
/// different code paths. Corresponds to Flutter's <c>cubic_bezier.dart</c> /
/// <c>path_tessellation.dart</c>.
/// </summary>
public sealed class CubicBezierScene : ISkiaScene
{
	public string Name => "CubicBezier";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			StrokeCap = SKStrokeCap.Round,
		};

		var palette = new[]
		{
			SKColors.Crimson, SKColors.RoyalBlue, SKColors.ForestGreen,
			SKColors.DarkOrange, SKColors.Purple, SKColors.Teal,
		};

		for (var i = 0; i < 200; i++)
		{
			var x0 = (i * 11) % 256;
			var y0 = (i * 17) % 256;
			var x1 = (i * 37) % 256;
			var y1 = (i * 29) % 256;
			var x2 = (i * 53) % 256;
			var y2 = (i * 41) % 256;
			var x3 = (i * 71) % 256;
			var y3 = (i * 61) % 256;

			using var builder = new SKPathBuilder();
			builder.MoveTo(x0, y0);
			builder.CubicTo(x1, y1, x2, y2, x3, y3);
			using var path = builder.Snapshot();

			stroke.Color = palette[i % palette.Length].WithAlpha(140);
			canvas.DrawPath(path, stroke);
		}
	}
}
