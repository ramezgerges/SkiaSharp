using System;
using System.IO;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Plays back a serialized <see cref="SKPicture"/> loaded from an <c>.skp</c>
/// file. One instance per file is registered by
/// <see cref="CapturedSceneRegistry"/>; the scene's <see cref="Name"/> is the
/// file's basename so the BenchmarkDotNet parameter matrix reads naturally
/// (<c>Scene: "Gallery.HomePage"</c>, <c>Scene: "Gallery.ListViewSample"</c>, …).
///
/// <para>
/// The trick: Uno Platform already produces an <c>SKPicture</c> for every UI
/// frame (see <c>Uno.UI/Helpers/SkiaRenderHelper.RecordPictureAndReturnPath</c>).
/// If Uno is patched to serialize that picture, dropping the resulting file
/// into this project's <c>Captures/</c> directory gives us a real-workload
/// benchmark row that replays identically on every backend — the picture is
/// backend-neutral by construction.
/// </para>
///
/// <para>
/// See <c>Captures/HOW_TO_CAPTURE.md</c> for the Uno-side hook.
/// </para>
/// </summary>
public sealed class PicturePlaybackScene : ISkiaScene
{
	public string Name { get; }
	public SKImageInfo Info { get; }

	private readonly SKPicture picture;

	public PicturePlaybackScene(string name, SKPicture picture, SKImageInfo info)
	{
		Name = name;
		this.picture = picture;
		Info = info;
	}

	public static PicturePlaybackScene FromFile(string path)
	{
		var name = "Captured." + Path.GetFileNameWithoutExtension(path);
		var bytes = File.ReadAllBytes(path);
		var picture = SKPicture.Deserialize(bytes)
			?? throw new InvalidDataException(
				$"Could not deserialize '{path}' as an SKPicture. Was it produced by a compatible SkiaSharp version?");

		// Snap the surface size to the picture's cull rect. SkPicture cull
		// rects come out of Uno as huge sentinel bounds (see Visual.InfiniteClipRect),
		// so cap them so the offscreen surface stays sane.
		var cull = picture.CullRect;
		var w = Math.Clamp((int)Math.Ceiling(cull.Width), 64, 4096);
		var h = Math.Clamp((int)Math.Ceiling(cull.Height), 64, 4096);
		var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
		return new PicturePlaybackScene(name, picture, info);
	}

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);
		canvas.DrawPicture(picture);
	}
}
