using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Draws content into a layer, then overlays a linear-gradient shader in
/// DstIn mode so the gradient's alpha acts as a mask on the content. This is
/// the classical "shader mask" pattern — the closest one-frame analog of
/// Flutter's <c>shader_mask_cache.dart</c> (Flutter's cache is widget-scoped;
/// here we test the layer + shader-blend fast path).
/// </summary>
public sealed class ShaderMaskScene : ISkiaScene
{
	public string Name => "ShaderMask";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		canvas.SaveLayer(null);

		// Content: a busy set of shapes.
		var swatches = new[]
		{
			SKColors.Crimson, SKColors.DarkOrange, SKColors.Goldenrod,
			SKColors.SeaGreen, SKColors.RoyalBlue, SKColors.Purple,
		};
		using (var fill = new SKPaint { IsAntialias = true })
		{
			for (var i = 0; i < 30; i++)
			{
				fill.Color = swatches[i % swatches.Length];
				var cx = ((i * 41) % 224) + 16;
				var cy = ((i * 59) % 224) + 16;
				canvas.DrawCircle(cx, cy, 20, fill);
			}
		}

		// Mask: horizontal gradient with alpha 0 at the edges, alpha 255 in the
		// middle. DstIn keeps content wherever the gradient has opacity.
		using (var gradient = SKShader.CreateLinearGradient(
			new SKPoint(0, 128), new SKPoint(256, 128),
			new[] { SKColors.Transparent, SKColors.Black, SKColors.Black, SKColors.Transparent },
			new[] { 0f, 0.25f, 0.75f, 1f },
			SKShaderTileMode.Clamp))
		using (var mask = new SKPaint { Shader = gradient, BlendMode = SKBlendMode.DstIn })
		{
			canvas.DrawRect(new SKRect(0, 0, 256, 256), mask);
		}

		canvas.Restore();
	}
}
