using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 60 small draws, each with a distinct combination of blend mode + shader
/// tile mode + gradient orientation. Forces Skia to allocate a large set of
/// pipeline states — Graphite pre-compiles or looks these up in a
/// well-tuned key/state cache, Ganesh tends to compile more lazily and pay
/// per-first-use latency.
///
/// <para>
/// After the un-timed warm-up frame in <c>GlobalSetup</c> both backends
/// have their pipelines resident, so the timed iterations measure the
/// steady-state lookup cost — that's the scenario Graphite's design
/// optimizes for.
/// </para>
/// </summary>
public sealed class PipelineDiversityScene : ISkiaScene
{
	public string Name => "PipelineDiversity";
	public SKImageInfo Info => new(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);

	private static readonly SKBlendMode[] Modes =
	{
		SKBlendMode.SrcOver, SKBlendMode.Plus, SKBlendMode.Multiply, SKBlendMode.Screen,
		SKBlendMode.Overlay, SKBlendMode.Darken, SKBlendMode.Lighten, SKBlendMode.ColorBurn,
		SKBlendMode.ColorDodge, SKBlendMode.HardLight, SKBlendMode.Difference, SKBlendMode.Exclusion,
	};

	private static readonly SKShaderTileMode[] TileModes =
	{
		SKShaderTileMode.Clamp, SKShaderTileMode.Repeat, SKShaderTileMode.Mirror, SKShaderTileMode.Decal,
	};

	private static readonly SKColor[][] Palettes =
	{
		new[] { SKColors.Crimson, SKColors.Goldenrod, SKColors.RoyalBlue },
		new[] { SKColors.Teal, SKColors.White, SKColors.HotPink },
		new[] { SKColors.Purple, SKColors.OrangeRed, SKColors.SeaGreen },
	};

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		const int cellW = 64, cellH = 64;
		for (var i = 0; i < 60; i++)
		{
			var col = i % 8;
			var row = i / 8;
			var x = col * cellW;
			var y = row * cellH;
			var rect = new SKRect(x, y, x + cellW, y + cellH);
			var center = new SKPoint(x + cellW / 2, y + cellH / 2);

			var palette = Palettes[i % Palettes.Length];
			var tileMode = TileModes[i % TileModes.Length];
			var blend = Modes[i % Modes.Length];

			using var shader = (i % 2) == 0
				? SKShader.CreateLinearGradient(
					new SKPoint(x, y), new SKPoint(x + cellW, y + cellH),
					palette, null, tileMode)
				: SKShader.CreateRadialGradient(center, cellW * 0.4f, palette, null, tileMode);
			using var paint = new SKPaint { Shader = shader, BlendMode = blend, IsAntialias = true };
			canvas.DrawRect(rect, paint);
		}
	}
}
