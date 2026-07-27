using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Draws content through a chained ImageFilter (ColorFilter → Blur → DropShadow).
/// The layered filter pipeline is where Graphite's ahead-of-time precompile
/// should show up strongest — corresponds to Flutter's
/// <c>animated_complex_image_filtered.dart</c> and <c>filtered_child_animation.dart</c>.
/// </summary>
public sealed class ImageFilterChainScene : ISkiaScene
{
	public string Name => "ImageFilterChain";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		// Boost saturation, then blur, then drop-shadow.
		var saturationMatrix = new float[]
		{
			1.6f, -0.3f, -0.3f, 0, 0,
		   -0.3f,  1.6f, -0.3f, 0, 0,
		   -0.3f, -0.3f,  1.6f, 0, 0,
			0,     0,     0,    1, 0,
		};
		using var colorFilter = SKColorFilter.CreateColorMatrix(saturationMatrix);
		using var colorStage = SKImageFilter.CreateColorFilter(colorFilter);
		using var blurStage = SKImageFilter.CreateBlur(3, 3, colorStage);
		using var shadowStage = SKImageFilter.CreateDropShadow(
			dx: 4, dy: 4, sigmaX: 2, sigmaY: 2,
			color: SKColors.Black.WithAlpha(120),
			input: blurStage);

		using var layerPaint = new SKPaint { ImageFilter = shadowStage };
		canvas.SaveLayer(layerPaint);

		var swatches = new[] { SKColors.Crimson, SKColors.RoyalBlue, SKColors.SeaGreen, SKColors.Goldenrod };
		using var fill = new SKPaint { IsAntialias = true };
		for (var i = 0; i < 12; i++)
		{
			fill.Color = swatches[i % swatches.Length];
			var cx = ((i * 47) % 216) + 20;
			var cy = ((i * 71) % 216) + 20;
			canvas.DrawCircle(cx, cy, 24, fill);
		}

		canvas.Restore();
	}
}
