using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A colored triangle mesh (grid of gouraud-shaded triangles). Exercises the
/// per-vertex-color fast path — corresponds to Flutter's <c>draw_vertices.dart</c>.
///
/// The grid is <c>16x16</c> quads → <c>17x17</c> vertices → <c>512</c> triangles.
/// </summary>
public sealed class DrawVerticesScene : ISkiaScene
{
	public string Name => "DrawVertices";
	public SKImageInfo Info => new(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);

		const int cells = 16;
		const int side = cells + 1;
		const float step = 256f / cells;

		var vertices = new SKPoint[side * side];
		var colors = new SKColor[side * side];
		for (var y = 0; y < side; y++)
		{
			for (var x = 0; x < side; x++)
			{
				var idx = y * side + x;
				vertices[idx] = new SKPoint(x * step, y * step);
				var r = (byte)((x * 255) / cells);
				var g = (byte)((y * 255) / cells);
				var b = (byte)(255 - ((x + y) * 255) / (2 * cells));
				colors[idx] = new SKColor(r, g, b, 255);
			}
		}

		// Two triangles per cell, wound consistently.
		var indices = new ushort[cells * cells * 6];
		var write = 0;
		for (var y = 0; y < cells; y++)
		{
			for (var x = 0; x < cells; x++)
			{
				var i0 = (ushort)(y * side + x);
				var i1 = (ushort)(y * side + x + 1);
				var i2 = (ushort)((y + 1) * side + x);
				var i3 = (ushort)((y + 1) * side + x + 1);
				indices[write++] = i0;
				indices[write++] = i1;
				indices[write++] = i2;
				indices[write++] = i2;
				indices[write++] = i1;
				indices[write++] = i3;
			}
		}

		using var paint = new SKPaint { IsAntialias = false };
		canvas.DrawVertices(SKVertexMode.Triangles, vertices, texs: null, colors, SKBlendMode.SrcOver, indices, paint);
	}
}
