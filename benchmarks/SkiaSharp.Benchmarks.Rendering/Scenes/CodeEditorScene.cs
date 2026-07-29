using SkiaSharp.Benchmarks.Rendering;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// A fake code-editor window with 44 lines of syntax-highlighted "code".
/// Each line is a sequence of coloured token runs (keyword blue, string
/// orange, comment green, identifier white, punctuation grey) plus a gutter
/// number and an optional squiggly-underline diagnostic. Roughly 12-18 text
/// runs per line, so a frame draws ~600-800 short DrawText calls with
/// unique colours — matches the paint-state-change hot path that every
/// real IDE (VS Code, Rider, Sublime, Neovim GUIs) hammers on scroll.
///
/// <para>
/// Flat top-level structure: each line renders at outer save depth, so
/// record→decompile→partition splits cleanly into per-line groups.
/// Direct <see cref="PartitionCount"/> defaults to 8; the parallel-graphite
/// sweep at N=4/8/12 should reveal whether the many-paint-changes-per-line
/// workload finally beats sequential.
/// </para>
/// </summary>
public sealed class CodeEditorScene : IPartitionedSkiaScene
{
	public string Name => "CodeEditor";
	public SKImageInfo Info => new(1024, 768, SKColorType.Rgba8888, SKAlphaType.Premul);
	public int PartitionCount => 8;

	private const int LineCount = 44;
	private const float LineHeight = 17f;
	private const float TopMargin = 12f;
	private const float LeftGutter = 42f;
	private const float FontSize = 12f;
	private const float TabIndent = 12f;

	// One Coke-can-dark background. IDE aesthetic — matches VS Code Dark+.
	private static readonly SKColor Background = new(0x1E, 0x1E, 0x1E);
	private static readonly SKColor GutterColor = new(0x2D, 0x2D, 0x2D);

	// Syntax token colours. Deliberately many distinct colours so every
	// line goes through several paint-cache lookups.
	private static readonly SKColor CommentColor = new(0x6A, 0x99, 0x55);   // green
	private static readonly SKColor KeywordColor = new(0x56, 0x9C, 0xD6);   // blue
	private static readonly SKColor TypeColor = new(0x4E, 0xC9, 0xB0);      // teal
	private static readonly SKColor StringColor = new(0xCE, 0x91, 0x78);    // orange
	private static readonly SKColor NumberColor = new(0xB5, 0xCE, 0xA8);    // lime
	private static readonly SKColor IdentColor = new(0x9C, 0xDC, 0xFE);     // sky
	private static readonly SKColor MethodColor = new(0xDC, 0xDC, 0xAA);    // yellow
	private static readonly SKColor PunctColor = new(0xC0, 0xC0, 0xC0);     // light grey
	private static readonly SKColor GutterText = new(0x85, 0x85, 0x85);
	private static readonly SKColor DiagnosticColor = new(0xF4, 0x47, 0x47); // red squiggle

	private enum Tok { Keyword, Type, Method, Ident, StrLit, NumLit, Punct, Comment, Whitespace }

	private readonly record struct Token(Tok Kind, string Text);

	// A hand-crafted mini "file" of C#-ish code — 44 lines of realistic-
	// looking token sequences. Not intended to compile; intended to have
	// dense, varied token coloring so recording exercises paint changes.
	private static readonly Token[][] Lines = BuildLines();

	private static Token[][] BuildLines() => new[]
	{
		L(C("// Skia's parallel-graphite recording benchmark.")),
		L(C("// Each line is drawn as a sequence of colored token runs")),
		L(C("// so glyph-record and paint-swap traffic are both realistic.")),
		L(),
		L(K("using"), W(), I("SkiaSharp"), P(";")),
		L(K("using"), W(), I("System"), P("."), I("Collections"), P("."), I("Generic"), P(";")),
		L(),
		L(K("namespace"), W(), I("SkiaSharp"), P("."), I("Benchmarks"), P("."), I("Rendering"), P(";")),
		L(),
		L(K("public"), W(), K("sealed"), W(), K("class"), W(), T("CodeEditorScene"), W(), P(":"), W(), T("IPartitionedSkiaScene")),
		L(P("{")),
		L(W(), W(), K("public"), W(), T("string"), W(), M("Name"), W(), P("=>"), W(), S("\"CodeEditor\""), P(";")),
		L(W(), W(), K("public"), W(), T("int"), W(), M("PartitionCount"), W(), P("=>"), W(), N("8"), P(";")),
		L(),
		L(W(), W(), K("private"), W(), K("const"), W(), T("int"), W(), M("LineCount"), W(), P("="), W(), N("44"), P(";")),
		L(W(), W(), K("private"), W(), K("const"), W(), T("float"), W(), M("LineHeight"), W(), P("="), W(), N("17f"), P(";")),
		L(),
		L(W(), W(), K("public"), W(), T("void"), W(), M("Draw"), P("("), T("SKCanvas"), W(), I("canvas"), P(")")),
		L(W(), W(), P("{")),
		L(W(), W(), W(), W(), I("canvas"), P("."), M("Clear"), P("("), I("Background"), P(")"), P(";")),
		L(W(), W(), W(), W(), K("for"), W(), P("("), K("var"), W(), I("i"), W(), P("="), W(), N("0"), P(";"), W(), I("i"), W(), P("<"), W(), M("PartitionCount"), P(";"), W(), I("i"), P("++"), P(")")),
		L(W(), W(), W(), W(), W(), W(), M("DrawPartition"), P("("), I("canvas"), P(","), W(), I("i"), P(")"), P(";")),
		L(W(), W(), P("}")),
		L(),
		L(W(), W(), C("// Body — partitions map 1:1 to distinct line ranges so")),
		L(W(), W(), C("// parallel recorders build independent op streams.")),
		L(W(), W(), K("public"), W(), T("void"), W(), M("DrawPartition"), P("("), T("SKCanvas"), W(), I("canvas"), P(","), W(), T("int"), W(), I("index"), P(")")),
		L(W(), W(), P("{")),
		L(W(), W(), W(), W(), K("var"), W(), I("start"), W(), P("="), W(), I("index"), W(), P("*"), W(), M("LineCount"), W(), P("/"), W(), M("PartitionCount"), P(";")),
		L(W(), W(), W(), W(), K("var"), W(), I("end"), W(), P("="), W(), P("("), I("index"), W(), P("+"), W(), N("1"), P(")"), W(), P("*"), W(), M("LineCount"), W(), P("/"), W(), M("PartitionCount"), P(";")),
		L(W(), W(), W(), W(), K("for"), W(), P("("), K("var"), W(), I("i"), W(), P("="), W(), I("start"), P(";"), W(), I("i"), W(), P("<"), W(), I("end"), P(";"), W(), I("i"), P("++"), P(")")),
		L(W(), W(), W(), W(), W(), W(), M("DrawLine"), P("("), I("canvas"), P(","), W(), I("i"), P(")"), P(";")),
		L(W(), W(), P("}")),
		L(),
		L(W(), W(), K("private"), W(), K("static"), W(), T("void"), W(), M("DrawLine"), P("("), T("SKCanvas"), W(), I("canvas"), P(","), W(), T("int"), W(), I("lineIndex"), P(")")),
		L(W(), W(), P("{")),
		L(W(), W(), W(), W(), C("// Emit the gutter number, then walk the token list.")),
		L(W(), W(), W(), W(), K("var"), W(), I("y"), W(), P("="), W(), M("TopMargin"), W(), P("+"), W(), P("("), I("lineIndex"), W(), P("+"), W(), N("1"), P(")"), W(), P("*"), W(), M("LineHeight"), P(";")),
		L(W(), W(), W(), W(), M("DrawGutter"), P("("), I("canvas"), P(","), W(), I("lineIndex"), P(","), W(), I("y"), P(")"), P(";")),
		L(W(), W(), W(), W(), M("DrawTokens"), P("("), I("canvas"), P(","), W(), I("lineIndex"), P(","), W(), I("y"), P(")"), P(";")),
		L(W(), W(), P("}")),
		L(P("}")),
		L(),
		L(C("// EOF")),
	};

	// Token constructors kept tiny to keep the file table above readable.
	private static Token K(string s) => new(Tok.Keyword, s);
	private static Token T(string s) => new(Tok.Type, s);
	private static Token M(string s) => new(Tok.Method, s);
	private static Token I(string s) => new(Tok.Ident, s);
	private static Token S(string s) => new(Tok.StrLit, s);
	private static Token N(string s) => new(Tok.NumLit, s);
	private static Token P(string s) => new(Tok.Punct, s);
	private static Token C(string s) => new(Tok.Comment, s);
	private static Token W() => new(Tok.Whitespace, " ");
	private static Token[] L(params Token[] tokens) => tokens;

	public void Draw(SKCanvas canvas)
	{
		canvas.Clear(Background);
		DrawGutterStrip(canvas);
		for (var i = 0; i < PartitionCount; i++)
			DrawPartition(canvas, i);
	}

	public void DrawPartition(SKCanvas canvas, int partitionIndex)
	{
		if (partitionIndex == 0)
		{
			canvas.Clear(Background);
			DrawGutterStrip(canvas);
		}

		var start = partitionIndex * LineCount / PartitionCount;
		var end = (partitionIndex + 1) * LineCount / PartitionCount;
		for (var i = start; i < end && i < Lines.Length; i++)
			DrawLine(canvas, i);
	}

	private static void DrawGutterStrip(SKCanvas canvas)
	{
		using var g = new SKPaint { Color = GutterColor };
		canvas.DrawRect(0, 0, LeftGutter - 6, 1024, g);
	}

	private static void DrawLine(SKCanvas canvas, int lineIndex)
	{
		var y = TopMargin + (lineIndex + 1) * LineHeight;

		using (var font = new SKFont { Size = FontSize - 1 })
		using (var paint = new SKPaint { IsAntialias = true, Color = GutterText })
			canvas.DrawText($"{lineIndex + 1,3}", LeftGutter - 10, y, SKTextAlign.Right, font, paint);

		// Sprinkle a squiggle underneath a few lines to exercise stroke+DPI
		// paint churn. Real IDEs do this per diagnostic; pick a stable
		// scattering so pixel output is reproducible.
		var wantSquiggle = (lineIndex * 13 % 7) == 0 && lineIndex > 4;
		var tokens = Lines[lineIndex];
		var x = LeftGutter + 4;
		using var font2 = new SKFont { Size = FontSize };
		foreach (var tok in tokens)
		{
			var color = ColorFor(tok.Kind);
			using (var paint = new SKPaint { IsAntialias = true, Color = color })
				canvas.DrawText(tok.Text, x, y, SKTextAlign.Left, font2, paint);
			x += MeasureRun(tok.Text, font2);
		}
		if (wantSquiggle)
			DrawSquiggle(canvas, LeftGutter + 4, y + 3, x, DiagnosticColor);
	}

	private static float MeasureRun(string s, SKFont font) => font.MeasureText(s);

	private static SKColor ColorFor(Tok kind) => kind switch
	{
		Tok.Keyword => KeywordColor,
		Tok.Type => TypeColor,
		Tok.Method => MethodColor,
		Tok.Ident => IdentColor,
		Tok.StrLit => StringColor,
		Tok.NumLit => NumberColor,
		Tok.Punct => PunctColor,
		Tok.Comment => CommentColor,
		Tok.Whitespace => IdentColor,
		_ => IdentColor,
	};

	private static void DrawSquiggle(SKCanvas canvas, float x0, float y, float x1, SKColor color)
	{
		using var path = new SKPath();
		var amp = 1.5f; var step = 3f; var dir = 1f;
		path.MoveTo(x0, y);
		for (var x = x0 + step; x <= x1; x += step)
		{
			path.LineTo(x, y + amp * dir);
			dir = -dir;
		}
		using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = color };
		canvas.DrawPath(path, paint);
	}
}
