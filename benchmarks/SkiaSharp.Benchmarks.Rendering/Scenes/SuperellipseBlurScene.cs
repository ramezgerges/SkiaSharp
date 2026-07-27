using System;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 20 squircle paths — a superellipse of order 4, approximated as a discretized
/// cubic-spline path — each drawn with an SKMaskFilter shadow-blur.
/// Skia has no rounded-superellipse primitive; this is the closest one-primitive
/// analog of Flutter's <c>rsuperellipse_blur.dart</c>.
/// </summary>
public sealed class SuperellipseBlurScene : ISkiaScene
{
	public string Name => "SuperellipseBlur";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	private readonly SKPath squirclePath = BuildSquircle(halfW: 24, halfH: 16, n: 4, segments: 48);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f);
		using var shadow = new SKPaint
		{
			IsAntialias = true,
			Color = SKColors.Black.WithAlpha(90),
			MaskFilter = blur,
		};
		using var fill = new SKPaint { IsAntialias = true };
		var palette = new[]
		{
			SKColors.Crimson, SKColors.DarkOrange, SKColors.SeaGreen,
			SKColors.RoyalBlue, SKColors.Purple,
		};

		for (var i = 0; i < 20; i++)
		{
			var col = i % 4;
			var row = i / 4;
			var cx = 40 + col * 56;
			var cy = 32 + row * 44;

			canvas.Save();
			canvas.Translate(cx + 3, cy + 3);
			canvas.DrawPath(squirclePath, shadow);
			canvas.Restore();

			canvas.Save();
			canvas.Translate(cx, cy);
			fill.Color = palette[i % palette.Length];
			canvas.DrawPath(squirclePath, fill);
			canvas.Restore();
		}
	}

	// Discretized superellipse |x/a|^n + |y/b|^n = 1 as a closed line loop.
	// n=4 gives a "squircle"; smaller n rounds towards a rectangle, larger n
	// approaches a diamond. 48 segments keeps antialiased curves smooth enough
	// that the sampling isn't visible.
	private static SKPath BuildSquircle(float halfW, float halfH, float n, int segments)
	{
		using var builder = new SKPathBuilder();
		for (var i = 0; i < segments; i++)
		{
			var t = (i / (float)segments) * MathF.PI * 2;
			var cos = MathF.Cos(t);
			var sin = MathF.Sin(t);
			var x = halfW * MathF.Sign(cos) * MathF.Pow(MathF.Abs(cos), 2f / n);
			var y = halfH * MathF.Sign(sin) * MathF.Pow(MathF.Abs(sin), 2f / n);
			if (i == 0)
				builder.MoveTo(x, y);
			else
				builder.LineTo(x, y);
		}
		builder.Close();
		return builder.Snapshot();
	}
}
