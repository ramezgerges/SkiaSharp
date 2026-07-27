using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A grid of six panels: three radial gradients and three sweep gradients with
/// varying color stops. Complements the existing <c>GradientBlend</c> (linear)
/// scene with the two other Skia gradient shaders — corresponds to Flutter's
/// <c>gradient_perf.dart</c>.
/// </summary>
public sealed class RadialSweepGradientScene : ISkiaScene
{
	public string Name => "RadialSweepGradient";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		var palettes = new[]
		{
			new[] { SKColors.Crimson, SKColors.Goldenrod, SKColors.MidnightBlue },
			new[] { SKColors.Teal, SKColors.White, SKColors.HotPink },
			new[] { SKColors.Purple, SKColors.OrangeRed, SKColors.SeaGreen, SKColors.RoyalBlue },
		};

		for (var i = 0; i < 6; i++)
		{
			var col = i % 3;
			var row = i / 3;
			var x = 8 + col * 84;
			var y = 8 + row * 124;
			var rect = new SKRect(x, y, x + 76, y + 116);
			var center = new SKPoint(x + 38, y + 58);
			var palette = palettes[i % palettes.Length];

			SKShader shader = row == 0
				? SKShader.CreateRadialGradient(center, 44, palette, null, SKShaderTileMode.Clamp)
				: SKShader.CreateSweepGradient(center, palette, null);

			using (shader)
			using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
			{
				canvas.DrawRect(rect, paint);
			}
		}
	}
}
