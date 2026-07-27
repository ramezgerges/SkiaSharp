using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A busy background overlaid by a rounded "frosted glass" panel whose
/// SaveLayerRec pulls the pixels beneath through a blur backdrop filter.
/// Corresponds to Flutter's <c>backdrop_filter.dart</c> / <c>animated_blur_backdrop_filter.dart</c>
/// — the layered composite where GPU backends should pull well ahead of raster.
/// </summary>
public sealed class BackdropBlurScene : ISkiaScene
{
	public string Name => "BackdropBlur";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		var swatches = new[] { SKColors.Crimson, SKColors.Goldenrod, SKColors.Teal, SKColors.Indigo, SKColors.OrangeRed };
		using (var fill = new SKPaint { IsAntialias = true })
		{
			for (var i = 0; i < 24; i++)
			{
				fill.Color = swatches[i % swatches.Length];
				var cx = ((i * 37) % 240) + 8;
				var cy = ((i * 53) % 240) + 8;
				canvas.DrawCircle(cx, cy, 32, fill);
			}
		}

		var panel = new SKRect(24, 84, 232, 172);
		using var blur = SKImageFilter.CreateBlur(12, 12);
		var rec = new SKCanvasSaveLayerRec
		{
			Bounds = panel,
			Backdrop = blur,
		};
		canvas.SaveLayer(in rec);
		canvas.Restore();

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2,
			Color = SKColors.White.WithAlpha(220),
		};
		canvas.DrawRoundRect(panel, 12, 12, stroke);
	}
}
