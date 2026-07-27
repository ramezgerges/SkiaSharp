using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Draws a pre-built 512x512 image 20 times, each scaled to ~48x48 with mipmap
/// sampling. Exercises the image-upload + mipmap-chain path — the closest
/// per-frame analog to Flutter's <c>large_images.dart</c>.
///
/// The source image is built once in the scene constructor (a stable palette
/// gradient with tile fill), so per-frame work is entirely on the draw side.
/// </summary>
public sealed class LargeImageScene : ISkiaScene
{
	public string Name => "LargeImage";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	private static readonly SKSamplingOptions MipmapSampling =
		new(SKFilterMode.Linear, SKMipmapMode.Linear);

	private readonly SKImage source = BuildSource();

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		for (var i = 0; i < 20; i++)
		{
			var x = ((i * 47) % 208);
			var y = ((i * 71) % 208);
			var dst = new SKRect(x, y, x + 48, y + 48);
			canvas.DrawImage(source, dst, MipmapSampling);
		}
	}

	private static SKImage BuildSource()
	{
		var info = new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info);
		var canvas = surface.Canvas;
		using (var gradient = SKShader.CreateLinearGradient(
			new SKPoint(0, 0), new SKPoint(512, 512),
			new[] { SKColors.Crimson, SKColors.Goldenrod, SKColors.SeaGreen, SKColors.RoyalBlue, SKColors.Purple },
			null, SKShaderTileMode.Clamp))
		using (var paint = new SKPaint { Shader = gradient })
		{
			canvas.DrawRect(new SKRect(0, 0, 512, 512), paint);
		}
		using (var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2,
			Color = SKColors.White.WithAlpha(160),
		})
		{
			for (var i = 0; i < 32; i += 4)
			{
				canvas.DrawLine(0, i * 16, 512, 512 - (i * 16), stroke);
				canvas.DrawLine(i * 16, 0, 512 - (i * 16), 512, stroke);
			}
		}
		return surface.Snapshot();
	}
}
