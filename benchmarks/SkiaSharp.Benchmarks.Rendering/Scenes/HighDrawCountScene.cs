using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// 5000 filled DrawRect calls with a shared paint. Isolates the per-draw
/// dispatch cost from any actual GPU work — the total drawing is tiny
/// (each rect is ~16×16), so anything the total time is telling us is
/// per-draw overhead in the backend.
///
/// <para>
/// Graphite's Recorder is architecturally cheaper per draw than Ganesh's
/// SurfaceDrawContext op-list — this is the scene where that difference
/// should show up cleanly. It's also a good stand-in for scroll frames in
/// a Xaml grid or ListView, which is a very real UI workload.
/// </para>
/// </summary>
public sealed class HighDrawCountScene : ISkiaScene
{
	public string Name => "HighDrawCount";
	public SKImageInfo Info => new(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);

	private static readonly SKColor[] Palette =
	{
		SKColors.Crimson, SKColors.OrangeRed, SKColors.Goldenrod, SKColors.SeaGreen,
		SKColors.Teal, SKColors.RoyalBlue, SKColors.Purple, SKColors.HotPink,
	};

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);
		using var paint = new SKPaint { IsAntialias = false };

		const int count = 5000;
		for (var i = 0; i < count; i++)
		{
			paint.Color = Palette[i % Palette.Length];
			var x = (i * 13) % 496;
			var y = ((i * 17) + (i * i / 31)) % 496;
			canvas.DrawRect(x, y, 16, 16, paint);
		}
	}
}
