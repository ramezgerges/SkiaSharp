using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A scale-up of <c>DrawAtlas</c> — 2000 sprites from a single atlas texture,
/// which lets us amortise per-draw driver overhead against a large batch of
/// identical-pipeline draws. Graphite's Recorder is designed to batch these
/// far more aggressively than Ganesh's op-list; <c>DrawAtlas</c> already
/// showed Graphite winning (0.85×) on the smaller version, so scaling the
/// batch is the cheapest way to widen the gap.
/// </summary>
public sealed class AtlasHeavyScene : ISkiaScene
{
	public string Name => "AtlasHeavy";
	public SKImageInfo Info => new(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);

	private static readonly SKColor[] TileColors =
	{
		SKColors.Crimson,    SKColors.OrangeRed, SKColors.Goldenrod, SKColors.YellowGreen,
		SKColors.SeaGreen,   SKColors.Teal,      SKColors.SteelBlue, SKColors.RoyalBlue,
		SKColors.MediumSlateBlue, SKColors.Purple, SKColors.HotPink, SKColors.DeepPink,
		SKColors.Chocolate,  SKColors.Peru,      SKColors.Sienna,    SKColors.Maroon,
	};

	private readonly SKImage atlas = BuildAtlas();

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		const int count = 2000;
		var sprites = new SKRect[count];
		var transforms = new SKRotationScaleMatrix[count];

		for (var i = 0; i < count; i++)
		{
			var tile = i % 16;
			var tx = (tile & 3) * 16;
			var ty = (tile >> 2) * 16;
			sprites[i] = new SKRect(tx, ty, tx + 16, ty + 16);

			var cx = ((i * 47) % 492) + 10;
			var cy = ((i * 71) % 492) + 10;
			var scale = 0.4f + ((i % 7) * 0.05f);
			var degrees = (i % 24) * 15f;
			transforms[i] = SKRotationScaleMatrix.CreateDegrees(scale, degrees, cx, cy, anchorX: 8, anchorY: 8);
		}

		canvas.DrawAtlas(atlas, sprites, transforms, SKSamplingOptions.Default, paint: null);
	}

	private static SKImage BuildAtlas()
	{
		var info = new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info);
		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Transparent);
		using var paint = new SKPaint { IsAntialias = false };
		for (var i = 0; i < 16; i++)
		{
			paint.Color = TileColors[i];
			var x = (i & 3) * 16;
			var y = (i >> 2) * 16;
			canvas.DrawRect(x, y, 16, 16, paint);
		}
		return surface.Snapshot();
	}
}
