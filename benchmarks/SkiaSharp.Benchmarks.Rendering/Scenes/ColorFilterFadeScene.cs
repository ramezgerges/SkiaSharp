using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 30 draws through a color-matrix filter that mimics a fade effect
/// (desaturate + dim). Exercises the ColorFilter attach-to-paint fast path —
/// corresponds to Flutter's <c>color_filter_and_fade.dart</c>.
/// </summary>
public sealed class ColorFilterFadeScene : ISkiaScene
{
	public string Name => "ColorFilterFade";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		// 50% desaturation + 30% brightness cut.
		const float lumaR = 0.2126f, lumaG = 0.7152f, lumaB = 0.0722f;
		const float sat = 0.5f;
		const float inv = 1f - sat;
		const float dim = 0.7f;
		var matrix = new float[]
		{
			(inv * lumaR + sat) * dim, inv * lumaG * dim,         inv * lumaB * dim,         0, 0,
			inv * lumaR * dim,         (inv * lumaG + sat) * dim, inv * lumaB * dim,         0, 0,
			inv * lumaR * dim,         inv * lumaG * dim,         (inv * lumaB + sat) * dim, 0, 0,
			0,                         0,                         0,                         1, 0,
		};
		using var filter = SKColorFilter.CreateColorMatrix(matrix);
		using var paint = new SKPaint { IsAntialias = true, ColorFilter = filter };

		var swatches = new[]
		{
			SKColors.Crimson, SKColors.OrangeRed, SKColors.Goldenrod, SKColors.SeaGreen,
			SKColors.Teal, SKColors.RoyalBlue, SKColors.Purple, SKColors.HotPink,
		};

		for (var i = 0; i < 30; i++)
		{
			paint.Color = swatches[i % swatches.Length];
			var cx = ((i * 41) % 216) + 20;
			var cy = ((i * 59) % 216) + 20;
			canvas.DrawCircle(cx, cy, 22, paint);
		}
	}
}
