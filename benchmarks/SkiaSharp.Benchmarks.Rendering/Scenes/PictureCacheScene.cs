using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Records a complex SKPicture once in the scene constructor, then replays it
/// 24 times per frame at different translations. Isolates picture-playback
/// cost from picture-recording cost — corresponds to Flutter's
/// <c>picture_cache.dart</c>.
/// </summary>
public sealed class PictureCacheScene : ISkiaScene
{
	public string Name => "PictureCache";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	private readonly SKPicture cached = Record();

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		for (var i = 0; i < 24; i++)
		{
			var tx = ((i * 37) % 208) - 24;
			var ty = ((i * 53) % 208) - 24;
			canvas.Save();
			canvas.Translate(tx, ty);
			canvas.DrawPicture(cached);
			canvas.Restore();
		}
	}

	private static SKPicture Record()
	{
		using var recorder = new SKPictureRecorder();
		var canvas = recorder.BeginRecording(new SKRect(0, 0, 64, 64));

		using (var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2,
			Color = SKColors.Crimson,
		})
		using (var fill = new SKPaint { IsAntialias = true })
		{
			fill.Color = SKColors.Goldenrod.WithAlpha(200);
			canvas.DrawCircle(32, 32, 20, fill);
			fill.Color = SKColors.RoyalBlue.WithAlpha(200);
			canvas.DrawRect(new SKRect(8, 8, 32, 32), fill);
			canvas.DrawLine(4, 4, 60, 60, stroke);
			canvas.DrawLine(60, 4, 4, 60, stroke);
			stroke.Color = SKColors.SeaGreen;
			canvas.DrawRoundRect(new SKRect(10, 34, 54, 58), 6, 6, stroke);
		}

		return recorder.EndRecording();
	}
}
