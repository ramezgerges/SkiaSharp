using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Eight nested SaveLayer calls, each with a translucent paint, each drawing
/// a shape into the layer. Exercises the layer optimizer's ability to peephole
/// through opacity stacks — corresponds to Flutter's <c>opacity_peephole.dart</c>
/// / <c>animated_complex_opacity.dart</c>.
/// </summary>
public sealed class OpacityLayersScene : ISkiaScene
{
	public string Name => "OpacityLayers";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		var swatches = new[]
		{
			SKColors.Crimson, SKColors.DarkOrange, SKColors.Goldenrod, SKColors.SeaGreen,
			SKColors.Teal, SKColors.RoyalBlue, SKColors.Purple, SKColors.HotPink,
		};

		const int layers = 8;
		for (var i = 0; i < layers; i++)
		{
			using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha(180) };
			canvas.SaveLayer(layerPaint);
		}

		using var fill = new SKPaint { IsAntialias = true };
		for (var i = 0; i < 40; i++)
		{
			fill.Color = swatches[i % swatches.Length];
			var cx = (i * 13) % 256;
			var cy = (i * 19) % 256;
			canvas.DrawCircle(cx, cy, 24, fill);
		}

		for (var i = 0; i < layers; i++)
			canvas.Restore();
	}
}
