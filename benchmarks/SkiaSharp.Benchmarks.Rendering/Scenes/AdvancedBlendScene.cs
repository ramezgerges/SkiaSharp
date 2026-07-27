using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A busy background overlaid by 30 shapes that cycle through non-SrcOver blend
/// modes (Plus, Overlay, Screen, HardLight, Multiply, ColorBurn, ColorDodge,
/// Difference). Each mode compiles a distinct shader program, so this stresses
/// the pipeline-cache + compile path — corresponds to Flutter's
/// <c>animated_advanced_blend.dart</c>.
/// </summary>
public sealed class AdvancedBlendScene : ISkiaScene
{
	public string Name => "AdvancedBlend";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	private static readonly SKBlendMode[] Modes =
	{
		SKBlendMode.Plus, SKBlendMode.Overlay, SKBlendMode.Screen, SKBlendMode.HardLight,
		SKBlendMode.Multiply, SKBlendMode.ColorBurn, SKBlendMode.ColorDodge, SKBlendMode.Difference,
	};

	private static readonly SKColor[] Colors =
	{
		SKColors.Crimson, SKColors.Goldenrod, SKColors.Teal, SKColors.Indigo,
		SKColors.OrangeRed, SKColors.SeaGreen, SKColors.RoyalBlue, SKColors.HotPink,
	};

	public void Draw(SKCanvas canvas)
	{
		using (var gradient = SKShader.CreateLinearGradient(
			new SKPoint(0, 0), new SKPoint(256, 256),
			new[] { SKColors.Navy, SKColors.OrangeRed, SKColors.White },
			new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp))
		using (var bg = new SKPaint { Shader = gradient })
		{
			canvas.DrawRect(new SKRect(0, 0, 256, 256), bg);
		}

		using var paint = new SKPaint { IsAntialias = true };
		for (var i = 0; i < 30; i++)
		{
			paint.Color = Colors[i % Colors.Length].WithAlpha(180);
			paint.BlendMode = Modes[i % Modes.Length];
			var cx = ((i * 43) % 216) + 20;
			var cy = ((i * 67) % 216) + 20;
			canvas.DrawCircle(cx, cy, 28, paint);
		}
	}
}
