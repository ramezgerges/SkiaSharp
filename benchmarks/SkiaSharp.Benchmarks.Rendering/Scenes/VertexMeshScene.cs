using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A single <see cref="SKCanvas.DrawVertices"/> call rendering a 60×45
/// vertex grid (~2,700 vertices, ~5,200 triangles) with per-vertex colours
/// derived from a smooth 2D wave function. Looks like a colourful terrain
/// heightmap or spectrogram.
///
/// <para>
/// Companion to <see cref="SpriteWallScene"/>. Same "explicit-batch API"
/// hypothesis: Skia's <c>DrawVertices</c> takes an entire pre-built mesh
/// in one call, so Graphite's dedicated <c>VerticesRenderStep</c> pipeline
/// packs the whole draw into a single indexed <c>vkCmdDrawIndexed</c>
/// without any per-primitive batching detection work. If the DrawAtlas
/// pattern generalises past textured quads to arbitrary meshes, this
/// scene should show a clear Graphite architectural win.
/// </para>
///
/// <para>
/// Original Flutter-ported <see cref="DrawVerticesScene"/> tested only
/// ~200 verts and Ganesh won slightly. At 5,000+ verts the fixed
/// Graphite advantage should amortise the same way <see cref="DrawAtlasScene"/>
/// (200 sprites, -15%) scaled to <see cref="SpriteWallScene"/> (2,000
/// sprites, -28%).
/// </para>
///
/// <para>
/// Mesh + colour buffer precomputed once in the constructor. Per-frame
/// work is a Clear + a single DrawVertices call. Deterministic — no
/// clock, no RNG.
/// </para>
/// </summary>
public sealed class VertexMeshScene : ISkiaScene
{
	public string Name => "VertexMesh";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);

	private const int Cols = 60;
	private const int Rows = 45;

	// Precomputed per-vertex arrays + index buffer. Using the array-form
	// DrawVertices overload rather than wrapping in an SKVertices object —
	// SKVertices.CreateCopy has a per-vertex-colour path that renders black
	// on this build; the array overload matches DrawVerticesScene which
	// works correctly.
	private readonly SKPoint[] positions;
	private readonly SKColor[] colors;
	private readonly ushort[] indices;

	public VertexMeshScene()
	{
		positions = new SKPoint[Cols * Rows];
		colors = new SKColor[Cols * Rows];

		// Vertex positions: uniform grid across the canvas, with a slight
		// perturbation per row so the mesh doesn't look like a plain
		// checkerboard when tinted. Colours: a smooth two-frequency wave
		// pattern so the mesh renders as visible colour bands.
		for (var r = 0; r < Rows; r++)
		{
			for (var c = 0; c < Cols; c++)
			{
				var i = r * Cols + c;
				var jitter = (r % 2 == 0) ? 0f : (1024f / (Cols - 1)) * 0.25f;
				var x = c * (1024f / (Cols - 1)) + jitter;
				var y = r * (768f / (Rows - 1));
				positions[i] = new SKPoint(x, y);

				// Smooth wave: two sines at different frequencies produce
				// hue bands that vary in both dimensions.
				var u = (float)c / (Cols - 1);
				var v = (float)r / (Rows - 1);
				var a = System.MathF.Sin(u * 7f + v * 3f);
				var b = System.MathF.Cos(v * 5f - u * 2f);
				var t = (a + b + 2f) * 0.25f; // 0..1
				colors[i] = HsvToRgb(t, 0.7f, 0.95f);
			}
		}

		// Triangle-list index buffer connecting the grid into 2 triangles
		// per quad. (Cols-1)×(Rows-1) quads × 6 indices = ~15,576 indices
		// = ~5,192 triangles.
		var indexCount = (Cols - 1) * (Rows - 1) * 6;
		indices = new ushort[indexCount];
		var k = 0;
		for (var r = 0; r < Rows - 1; r++)
		{
			for (var c = 0; c < Cols - 1; c++)
			{
				var tl = (ushort)(r * Cols + c);
				var tr = (ushort)(tl + 1);
				var bl = (ushort)((r + 1) * Cols + c);
				var br = (ushort)(bl + 1);
				indices[k++] = tl; indices[k++] = tr; indices[k++] = bl;
				indices[k++] = tr; indices[k++] = br; indices[k++] = bl;
			}
		}
	}

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(SKColors.White);
		// Paint colour is white and blend mode is Modulate so the per-vertex
		// colours pass through unchanged (white × vertex = vertex). Default
		// paint colour is opaque black; SrcOver of black-over-vertex produces
		// black — the classic DrawVertices footgun.
		using var paint = new SKPaint { IsAntialias = false, Color = SKColors.White };
		canvas.DrawVertices(SKVertexMode.Triangles, positions, texs: null, colors, SKBlendMode.Modulate, indices, paint);
	}

	// Small helper — HSV → RGB without going through SKColor.FromHsv (which
	// uses a slower path). h/s/v in [0,1]; returns opaque SKColor.
	private static SKColor HsvToRgb(float h, float s, float v)
	{
		var c = v * s;
		var hp = h * 6f;
		var x = c * (1 - System.MathF.Abs(hp % 2 - 1));
		float rp = 0, gp = 0, bp = 0;
		if (hp < 1) { rp = c; gp = x; }
		else if (hp < 2) { rp = x; gp = c; }
		else if (hp < 3) { gp = c; bp = x; }
		else if (hp < 4) { gp = x; bp = c; }
		else if (hp < 5) { rp = x; bp = c; }
		else { rp = c; bp = x; }
		var m = v - c;
		return new SKColor(
			(byte)System.Math.Clamp((rp + m) * 255f, 0, 255),
			(byte)System.Math.Clamp((gp + m) * 255f, 0, 255),
			(byte)System.Math.Clamp((bp + m) * 255f, 0, 255));
	}
}
