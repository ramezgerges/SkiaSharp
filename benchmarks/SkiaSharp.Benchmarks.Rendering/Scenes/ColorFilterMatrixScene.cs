using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// The same ColorMatrix filter reused across 60 draws. A backend that lazily
/// caches the filter's shader keeps up; one that recompiles per-draw won't.
/// Closest Skia equivalent of Flutter's <c>color_filter_cache.dart</c>
/// (Flutter's cache is widget-level; here we test the paint-level fast path).
/// </summary>
public sealed class ColorFilterMatrixScene : ISkiaScene
{
	public string Name => "ColorFilterMatrix";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		// Hue-rotate ~120°.
		var matrix = new float[]
		{
			-0.5f,  0.5f,  1f, 0, 0,
			 1f,   -0.5f,  0.5f, 0, 0,
			 0.5f,  1f,   -0.5f, 0, 0,
			 0,     0,     0,    1, 0,
		};
		using var filter = SKColorFilter.CreateColorMatrix(matrix);
		using var paint = new SKPaint { IsAntialias = true, ColorFilter = filter };

		var swatches = new[]
		{
			SKColors.Crimson, SKColors.OrangeRed, SKColors.Goldenrod, SKColors.YellowGreen,
			SKColors.SeaGreen, SKColors.Teal, SKColors.RoyalBlue, SKColors.HotPink,
		};

		for (var i = 0; i < 60; i++)
		{
			paint.Color = swatches[i % swatches.Length];
			var x = ((i * 29) % 224) + 12;
			var y = ((i * 41) % 224) + 12;
			canvas.DrawRect(x, y, 20, 20, paint);
		}
	}
}
