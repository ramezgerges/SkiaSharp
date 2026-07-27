using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A grid of rounded rectangles drawn with an SKMaskFilter blur, mimicking a
/// drop-shadow row. Exercises the mask-filter fast path — corresponds to
/// Flutter's <c>rrect_blur.dart</c>.
/// </summary>
public sealed class RRectBlurScene : ISkiaScene
{
	public string Name => "RRectBlur";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
		using var shadow = new SKPaint
		{
			IsAntialias = true,
			Color = SKColors.Black.WithAlpha(80),
			MaskFilter = blur,
		};
		using var fill = new SKPaint { IsAntialias = true };

		var swatches = new[]
		{
			SKColors.Crimson, SKColors.DarkOrange, SKColors.SeaGreen,
			SKColors.RoyalBlue, SKColors.Purple,
		};

		const int rows = 5, cols = 4;
		var rectW = 48f;
		var rectH = 32f;
		var stepX = (256f - (cols * rectW)) / (cols + 1);
		var stepY = (256f - (rows * rectH)) / (rows + 1);

		for (var r = 0; r < rows; r++)
		{
			for (var c = 0; c < cols; c++)
			{
				var x = stepX + c * (rectW + stepX);
				var y = stepY + r * (rectH + stepY);
				var rect = new SKRect(x, y, x + rectW, y + rectH);
				var shadowOffset = new SKRect(x + 3, y + 3, x + 3 + rectW, y + 3 + rectH);

				canvas.DrawRoundRect(shadowOffset, 8, 8, shadow);
				fill.Color = swatches[(r * cols + c) % swatches.Length];
				canvas.DrawRoundRect(rect, 8, 8, fill);
			}
		}
	}
}
