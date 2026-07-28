using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 50 distinct source images drawn once each — every image is unique, so the
/// image-provider callback (which caches uploads by SKImage.UniqueId) fires
/// once per image and never repeats. Tests each backend's ability to manage
/// texture lifetime + upload without recompiling or thrashing.
///
/// <para>
/// The images are built once in the scene constructor, so per-frame work is
/// pure GPU-side texture upload + draw. Ganesh's texture cache handles this
/// case fine, but Graphite's explicit resource-lifetime model should
/// pipeline the uploads slightly better on modern APIs (Vulkan/Metal/D3D12)
/// where the driver can plan the transfers upfront.
/// </para>
/// </summary>
public sealed class ManyImagesScene : ISkiaScene
{
	public string Name => "ManyImages";
	public SKImageInfo Info => new(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);

	private readonly SKImage[] images = BuildImages();

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		for (var i = 0; i < images.Length; i++)
		{
			var x = ((i * 47) % 448);
			var y = ((i * 71) % 448);
			canvas.DrawImage(images[i], new SKRect(x, y, x + 64, y + 64), SKSamplingOptions.Default);
		}
	}

	private static SKImage[] BuildImages()
	{
		const int count = 50;
		var result = new SKImage[count];
		var info = new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
		for (var i = 0; i < count; i++)
		{
			using var surface = SKSurface.Create(info);
			var canvas = surface.Canvas;
			// Distinct radial gradient per image so each is a genuinely different texture.
			using var shader = SKShader.CreateRadialGradient(
				new SKPoint(32, 32), 40,
				new[]
				{
					new SKColor((byte)((i * 71) & 0xFF), (byte)((i * 13) & 0xFF), (byte)((i * 191) & 0xFF), 255),
					new SKColor((byte)((i * 191) & 0xFF), (byte)((i * 71) & 0xFF), (byte)((i * 13) & 0xFF), 255),
					SKColors.White,
				}, null, SKShaderTileMode.Clamp);
			using var paint = new SKPaint { Shader = shader };
			canvas.DrawRect(new SKRect(0, 0, 64, 64), paint);
			result[i] = surface.Snapshot();
		}
		return result;
	}
}
