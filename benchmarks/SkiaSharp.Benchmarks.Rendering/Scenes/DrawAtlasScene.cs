using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Builds a small atlas image once, then DrawAtlas 200 rotated + scaled sprites
/// from it. Exercises the batched textured-draw path — corresponds to Flutter's
/// <c>draw_atlas.dart</c>.
///
/// The atlas is a 64x64 image split into a 4x4 grid of 16x16 colored tiles. Both
/// the atlas construction and the transform/sprite arrays are deterministic — no
/// clock, no RNG.
/// </summary>
public sealed class DrawAtlasScene : ISkiaScene
{
	public string Name => "DrawAtlas";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

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

		const int count = 200;
		var sprites = new SKRect[count];
		var transforms = new SKRotationScaleMatrix[count];

		for (var i = 0; i < count; i++)
		{
			var tile = i % 16;
			var tx = (tile & 3) * 16;
			var ty = (tile >> 2) * 16;
			sprites[i] = new SKRect(tx, ty, tx + 16, ty + 16);

			// Deterministic pseudo-scatter: sinusoid-free integer hash → position.
			var cx = ((i * 47) % 236) + 10;
			var cy = ((i * 71) % 236) + 10;
			var scale = 0.6f + ((i % 7) * 0.08f);
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
