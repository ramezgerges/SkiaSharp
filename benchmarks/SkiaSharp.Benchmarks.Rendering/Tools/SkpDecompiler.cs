using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkiaSharp.Benchmarks.Rendering.Tools;

/// <summary>
/// Parses an <c>.skp</c> file (Skia picture stream) and emits equivalent C#
/// source code that reconstructs the drawing via <see cref="SKCanvas"/> calls.
///
/// <para>
/// Format map (from <c>externals/skia/src/core/SkPicture.cpp</c>,
/// <c>SkPictureData.cpp</c>, <c>SkPictureFlat.h</c>, <c>SkPaintPriv.cpp</c>,
/// <c>SkWriteBuffer.cpp</c>):
/// </para>
///
/// <code>
///   header:
///     magic "skiapict"      8 bytes
///     version              uint32 LE
///     cull rect            4 × float32 LE
///     trailing byte        =1 for SkPictureData
///
///   outer stream (each entry: FourCC tag + uint32 size + payload):
///     "read"   size = bytes of op stream
///     "fact"   size = count of factory-name strings (writeString entries)
///     "tpfc"   size = count of typefaces
///     "aray"   size = bytes of the resource-buffer payload
///     "pctr"   size = count of sub-pictures (recurse)
///     "eof "   size = 0 (terminator)
///
///   resource buffer (inside "aray", writes via SkBinaryWriteBuffer — all
///   4-byte aligned; each of these is a tag pair inside the buffer):
///     "pnt "   count = num paints
///     "pth "   count = num paths; then num-paths repeated as an int; then paths
///     "blob"   count = num text blobs
///     "vert"   count = num vertices
///     "imag"   count = num images
///
///   op stream (inside "read"):
///     each op word: uint32 LE, high byte = DrawType (SkPictureFlat.h),
///     low 24 bits = total byte size of this op including the op word. Args
///     follow, laid out per op_code as in SkPicturePlayback::handleOp.
///
///   SkPaint (in "pnt " entries — SkPaintPriv::Flatten):
///     strokeWidth float32
///     strokeMiter float32
///     color4f     4 × float32 (R,G,B,A)
///     packed      uint32
///       bits  0-7 : flags   (bit0 = antiAlias, bit1 = dither)
///       bits  8-15: blend   (or 0xFF sentinel for custom blender)
///       bits 16-17: strokeCap
///       bits 18-19: strokeJoin
///       bits 20-21: style
///       bits 24-31: flatFlags (bit0 = hasTypeface, bit1 = hasEffects)
///     if hasEffects: 6 × writeFlattenable — pathEffect, shader, maskFilter,
///     colorFilter, imageFilter, blender. writeFlattenable writes 0 for null,
///     else: factory-index (uint32) + payload-size (uint32) + payload bytes.
/// </code>
///
/// <para>
/// Paints come out as fully-formed <c>SKPaint</c> constructor literals for
/// the plain fields (color, alpha, style, stroke, blend, antialias). Nested
/// flattenable effects (shaders, filters) are annotated with their factory
/// name and payload size, but the payloads themselves are not decoded — the
/// SkFlattenable subclass zoo is a deeper rabbit hole and left as future
/// work. Path/blob/image tables come out as commented placeholders showing
/// counts + byte extents.
/// </para>
/// </summary>
public static class SkpDecompiler
{
	private static readonly (string Name, byte Code)[] KnownOps =
	{
		("UNUSED", 0), ("CLIP_PATH", 1), ("CLIP_REGION", 2), ("CLIP_RECT", 3),
		("CLIP_RRECT", 4), ("CONCAT", 5), ("DRAW_CLEAR", 10), ("DRAW_DATA", 11),
		("DRAW_OVAL", 12), ("DRAW_PAINT", 13), ("DRAW_PATH", 14), ("DRAW_PICTURE", 15),
		("DRAW_POINTS", 16), ("DRAW_RECT", 21), ("DRAW_RRECT", 22),
		("RESTORE", 28), ("ROTATE", 29), ("SAVE", 30), ("SCALE", 32),
		("SET_MATRIX", 33), ("SKEW", 34), ("TRANSLATE", 35), ("NOOP", 36),
		("DRAW_DRRECT", 40), ("DRAW_PATCH", 43), ("DRAW_PICTURE_MATRIX_PAINT", 44),
		("DRAW_TEXT_BLOB", 45), ("DRAW_IMAGE", 46), ("DRAW_ATLAS", 48),
		("DRAW_IMAGE_NINE", 49), ("DRAW_IMAGE_RECT", 50),
		("SAVE_LAYER_SAVELAYERREC", 52), ("DRAW_ANNOTATION", 53),
		("DRAW_DRAWABLE", 54), ("DRAW_DRAWABLE_MATRIX", 55),
		("DRAW_SHADOW_REC", 58), ("DRAW_IMAGE_LATTICE", 59),
		("DRAW_ARC", 60), ("DRAW_REGION", 61), ("DRAW_VERTICES_OBJECT", 62),
		("FLUSH", 63), ("DRAW_EDGEAA_IMAGE_SET", 64), ("SAVE_BEHIND", 65),
		("DRAW_EDGEAA_QUAD", 66), ("DRAW_BEHIND_PAINT", 67), ("CONCAT44", 68),
		("CLIP_SHADER_IN_PAINT", 69), ("SET_M44", 71), ("DRAW_IMAGE2", 72),
		("DRAW_IMAGE_RECT2", 73), ("DRAW_IMAGE_LATTICE2", 74),
		("DRAW_EDGEAA_IMAGE_SET2", 75), ("RESET_CLIP", 76), ("DRAW_SLUG", 77),
	};

	private static readonly Dictionary<byte, string> OpNames = MakeOpMap();

	private static Dictionary<byte, string> MakeOpMap()
	{
		var d = new Dictionary<byte, string>();
		foreach (var (name, code) in KnownOps) d[code] = name;
		return d;
	}

	private sealed record ParsedPaint(
		float StrokeWidth,
		float StrokeMiter,
		float R, float G, float B, float A,
		bool AntiAlias, bool Dither,
		int BlendMode, // -1 for "custom blender" sentinel
		int StrokeCap, int StrokeJoin, int Style,
		bool HasEffects,
		ParsedFlattenable?[] Effects); // one per: pathEffect, shader, maskFilter, colorFilter, imageFilter, blender

	// Flattenable = anything SkWriteBuffer::writeFlattenable serialises: shaders,
	// mask filters, image filters, color filters, path effects, blenders. Each
	// specific subclass overrides ::flatten with its own payload; we decode
	// only the ones Uno's captures reference and leave the rest as `Unknown`.
	private abstract record ParsedFlattenable(string FactoryName);
	private sealed record UnknownFlattenable(string FactoryName, byte[] Payload) : ParsedFlattenable(FactoryName);
	// SkModeColorFilter / SkBlendModeColorFilter: color4f + blend mode.
	private sealed record ModeColorFilter(float R, float G, float B, float A, int BlendMode) : ParsedFlattenable("SkModeColorFilter");
	// SkImageFilter base header: N inputs (each optional flattenable). Modern
	// versions (>= kRemoveDeprecatedCropRect=103) omit the old crop rect.
	// Then per-subclass fields.
	private sealed record BlurImageFilter(ParsedFlattenable?[] Inputs, float SigmaX, float SigmaY, int TileMode) : ParsedFlattenable("SkBlurImageFilterImpl");
	private sealed record ColorFilterImageFilter(ParsedFlattenable?[] Inputs, ParsedFlattenable? ColorFilter) : ParsedFlattenable("SkColorFilterImageFilterImpl");
	private sealed record MatrixTransformImageFilter(ParsedFlattenable?[] Inputs, float[] Matrix9, ParsedSampling Sampling) : ParsedFlattenable("SkMatrixTransformImageFilter");
	private sealed record ComposeImageFilter(ParsedFlattenable?[] Inputs) : ParsedFlattenable("SkComposeImageFilterImpl");
	private sealed record ParsedSampling(int MaxAniso, bool UseCubic, float CubicB, float CubicC, int Filter, int Mipmap);

	private sealed class ParsedSkp
	{
		public uint Version;
		public float CullLeft, CullTop, CullRight, CullBottom;
		public byte[] OpData = Array.Empty<byte>();
		public List<string> FactoryNames = new();
		public List<ParsedPaint> Paints = new();
		public List<ParsedPath> Paths = new();
		public List<ParsedImage> Images = new();
		public List<ParsedTextBlob> TextBlobs = new();
		public int PathCount, TextBlobCount, VerticesCount, ImageCount;
		public int SubPictureCount;
		public long ArrayBufferBytes;

		// Sub-pictures parsed recursively from the "pctr" tag. Local index in
		// the op stream (1-based) — SubPictures[picIdx - 1].
		public List<ParsedSkp> SubPictures = new();

		// Typefaces live at the TOP-LEVEL only in modern .skp files (>v43); a
		// sub-picture's SkFont typeface index refers into this outer array.
		// Only the top-level ParsedSkp populates this — sub-pictures see it
		// via `TopLevel` back-reference below.
		public List<ParsedTypeface> Typefaces = new();

		// Back-reference to the top-level picture; used at emit time so
		// sub-picture text-blob code can find the shared typeface table.
		public ParsedSkp? TopLevel;

		// Pre-order-assigned unique ID across the whole tree. Top-level is 0;
		// each sub-picture gets a distinct positive ID that names its
		// emitted C# class (SubPic_N).
		public int GlobalId;

		// If parsing bailed partway through (typefaces we can't skip, or
		// a nested sub-picture that couldn't be parsed), we still emit a
		// stub class for it — but its Draw() is a no-op.
		public bool Unparseable;
		public string BailReason = "";
	}

	// One entry per top-level typeface. FontData is the embedded font blob
	// when the .skp was written with kDoIncludeData (Skia's default) — that's
	// what we hand to SKTypeface.FromData at scene-construction time.
	private sealed record ParsedTypeface(string FamilyName, int Weight, int Width, int SlantEnum, byte[]? FontData);

	// One entry per path in the aray "pth " table. Two shapes: general
	// (verbs + points + weights) and rrect. Fill types match Skia's enum:
	// 0=Winding, 1=EvenOdd, 2=InverseWinding, 3=InverseEvenOdd.
	private abstract record ParsedPath(int FillType);
	private sealed record ParsedGeneralPath(int FillType, float[] Points, float[] Conics, byte[] Verbs) : ParsedPath(FillType);
	// SkRRect layout: 4 floats for rect (l,t,r,b), 8 floats for 4 corner radii (x,y each).
	private sealed record ParsedRRectPath(int FillType, int Direction, float[] Rrect12, int Start) : ParsedPath(FillType);

	// Encoded image blob straight from aray "imag". Payload is PNG/JPEG bytes;
	// hand it to SKImage.FromEncodedData at scene-construction time.
	private sealed record ParsedImage(byte[] EncodedData, bool Unpremul);

	// Text blob = a bounds rect plus a sequence of runs.
	private sealed record ParsedTextBlob(float BoundsLeft, float BoundsTop, float BoundsRight, float BoundsBottom, List<ParsedTextRun> Runs);
	// Each run positions `GlyphCount` glyphs of a specific font.
	//   Positioning: 0=default (all glyphs use offset), 1=horizontal (one X per glyph, shared Y),
	//                2=full (X+Y per glyph), 3=RSXform (4 floats per glyph).
	private sealed record ParsedTextRun(int GlyphCount, int Positioning, float OffsetX, float OffsetY, ParsedFont Font, ushort[] Glyphs, float[] Positions);
	// SkFont: size + optional scaleX/skewX + optional typeface index (1-based
	// into ParsedSkp.TopLevel.Typefaces; 0 means null typeface → system default).
	private sealed record ParsedFont(float Size, float ScaleX, float SkewX, byte Flags, byte Edging, byte Hinting, int TypefaceIndex);

	public static string Decompile(byte[] skpBytes, string className = "DecompiledScene")
	{
		var parsed = Parse(skpBytes);
		return Emit(parsed, className);
	}

	/// <summary>
	/// Same as <see cref="Decompile"/>, but splits the decoded op stream into
	/// <paramref name="partitionCount"/> chunks at balanced Save/Restore
	/// boundaries and emits an <c>IPartitionedSkiaScene</c> whose
	/// <c>DrawPartition(canvas, i)</c> replays only the i-th chunk. If the
	/// stream has fewer natural balanced points than requested, fewer
	/// partitions are produced (never more than the stream allows).
	/// </summary>
	public static string DecompilePartitioned(byte[] skpBytes, string className, int partitionCount)
	{
		if (partitionCount < 1) throw new ArgumentOutOfRangeException(nameof(partitionCount));
		var parsed = Parse(skpBytes);
		if (partitionCount == 1) return Emit(parsed, className);
		return EmitPartitioned(parsed, className, partitionCount);
	}

	// ────────────────────────────────────────────────────────────────────────
	// Parsing
	// ────────────────────────────────────────────────────────────────────────

	private static ParsedSkp Parse(byte[] skpBytes)
	{
		using var stream = new MemoryStream(skpBytes);
		using var reader = new BinaryReader(stream);
		var top = ParsePicture(reader);

		// Walk the tree once to hand every picture a unique global ID and
		// a back-reference to the top-level. The emitter uses both: IDs to
		// name emitted classes (SubPic_1…), TopLevel so sub-pictures'
		// text-blob code can find the shared typeface table.
		int nextId = 0;
		AssignGlobalIdsAndTopLevel(top, top, ref nextId);
		return top;
	}

	private static void AssignGlobalIdsAndTopLevel(ParsedSkp p, ParsedSkp top, ref int nextId)
	{
		p.GlobalId = nextId++;
		p.TopLevel = top;
		foreach (var sub in p.SubPictures)
			AssignGlobalIdsAndTopLevel(sub, top, ref nextId);
	}

	// Reads one complete SkPicture (magic + header + tags + eof) from the
	// current stream position. Called both for the top-level picture and
	// recursively for every "pctr" sub-picture — Skia serializes them all
	// with the stream-level format (full magic + header).
	private static ParsedSkp ParsePicture(BinaryReader reader)
	{
		var stream = reader.BaseStream;

		var magic = reader.ReadBytes(8);
		if (Encoding.ASCII.GetString(magic) != "skiapict")
			throw new InvalidDataException("Not an .skp file: magic mismatch");

		var p = new ParsedSkp
		{
			Version = reader.ReadUInt32(),
			CullLeft = reader.ReadSingle(),
			CullTop = reader.ReadSingle(),
			CullRight = reader.ReadSingle(),
			CullBottom = reader.ReadSingle(),
		};

		var trailing = reader.ReadByte();
		if (trailing != 1)
			throw new NotSupportedException($"Trailing byte {trailing} — only SkPictureData (=1) is supported");

		while (stream.Position < stream.Length)
		{
			var tag = ReadTag(reader);
			if (tag == "eof ") break; // EOF is bare — no size field follows.
			var size = reader.ReadUInt32();

			switch (tag)
			{
				case "read":
					p.OpData = reader.ReadBytes((int)size);
					break;
				case "fact":
				{
					// "fact" size is BYTES on the outer stream. Payload is:
					//   uint32 count
					//   for each: writePackedUInt(len) then `len` bytes of name.
					// Names are NOT padded to any alignment.
					var payload = reader.ReadBytes((int)size);
					using var fs = new MemoryStream(payload);
					using var fr = new BinaryReader(fs);
					var count = fr.ReadUInt32();
					for (var i = 0; i < count; i++)
					{
						var len = (int)ReadPackedUInt(fr);
						var nameBytes = fr.ReadBytes(len);
						p.FactoryNames.Add(Encoding.UTF8.GetString(nameBytes));
					}
					break;
				}
				case "tpfc":
					// size = COUNT of typefaces (not bytes). Each is a
					// SkFontDescriptor::serialize()-encoded blob. Modern .skp
					// files (>v43) put all typefaces at top level and sub-
					// picture tpfc tags carry size=0, so this loop only fires
					// on the outer picture. We capture family+style plus the
					// optional embedded font-data blob so the emitter can
					// reconstruct real SKTypeface objects at run time.
					for (var i = 0; i < size; i++)
					{
						try { p.Typefaces.Add(DecodeTypeface(reader)); }
						catch (Exception ex)
						{
							p.Unparseable = true;
							p.BailReason = $"typeface #{i} decode failed: {ex.Message}";
							return p;
						}
					}
					continue;
				case "aray":
					p.ArrayBufferBytes = size;
					ParseArrayBuffer(reader.ReadBytes((int)size), p);
					break;
				case "pctr":
					// size = COUNT of sub-pictures. Each is a full serialized
					// SkPicture starting with "skiapict" magic. Recurse into
					// each. If one bails, we can't trust the stream position
					// beyond it, so we stop reading further tags on this level.
					p.SubPictureCount = (int)size;
					for (var i = 0; i < size; i++)
					{
						ParsedSkp sub;
						try { sub = ParsePicture(reader); }
						catch (Exception ex)
						{
							sub = new ParsedSkp { Unparseable = true, BailReason = ex.Message };
							p.SubPictures.Add(sub);
							p.Unparseable = true;
							p.BailReason = $"sub-picture #{i}: {ex.Message}";
							return p;
						}
						p.SubPictures.Add(sub);
						if (sub.Unparseable)
						{
							p.Unparseable = true;
							p.BailReason = $"sub-picture #{i}: {sub.BailReason}";
							return p;
						}
					}
					break;
				default:
					// Unknown tag — skip whatever body we can and stop, since we
					// don't know how to skip typefaces reliably below.
					stream.Position += size;
					break;
			}
		}
		return p;
	}

	private static void ParseArrayBuffer(byte[] buf, ParsedSkp p)
	{
		using var s = new MemoryStream(buf);
		using var r = new BinaryReader(s);

		while (s.Position < s.Length)
		{
			var tag = ReadTag(r);
			var count = r.ReadUInt32();

			switch (tag)
			{
				case "pnt ":
					for (var i = 0; i < count; i++)
						p.Paints.Add(ReadPaint(r, p.FactoryNames));
					break;
				case "pth ":
					// SkPictureData writes the count TAG then writes it AGAIN as
					// an int32 payload prefix before the paths themselves.
					p.PathCount = (int)count;
					var pathCountAgain = r.ReadInt32();
					for (var i = 0; i < pathCountAgain; i++)
						p.Paths.Add(DecodePath(r));
					// Paths pad to 4-byte alignment at each writePath boundary,
					// so no extra realignment needed here.
					break;
				case "blob":
					p.TextBlobCount = (int)count;
					for (var i = 0; i < count; i++)
						p.TextBlobs.Add(DecodeTextBlob(r));
					break;
				case "vert":
					p.VerticesCount = (int)count;
					return; // vertices unresolvable — stop
				case "imag":
					p.ImageCount = (int)count;
					for (var i = 0; i < count; i++)
						p.Images.Add(DecodeImage(r));
					break;
				case "slug":
					return; // count of slugs; skip
				default:
					return; // unknown — stop cleanly
			}
		}
	}

	// ────────────────────────────────────────────────────────────────────────
	// Path decoder — mirrors SkPath::ReadFromMemory in SkPath_serial.cpp
	// ────────────────────────────────────────────────────────────────────────

	private static ParsedPath DecodePath(BinaryReader r)
	{
		var start = r.BaseStream.Position;
		var packed = r.ReadUInt32();
		var version = (int)(packed & 0xFF);
		if (version != 4 && version != 5)
			throw new InvalidDataException($"unsupported path version {version} at offset {start}");
		var fillType = (int)((packed >> 8) & 0x3);
		var serType = (int)((packed >> 28) & 0xF);

		ParsedPath result;
		if (serType == 1)
		{
			// RRect path: 12 floats (rect + radii) + int32 start, padded to 4.
			var rrect = new float[12];
			for (var i = 0; i < 12; i++) rrect[i] = r.ReadSingle();
			var startIdx = r.ReadInt32();
			var dir = (int)((packed >> 26) & 0x3);
			result = new ParsedRRectPath(fillType, dir, rrect, startIdx);
		}
		else if (serType == 0)
		{
			// General path: pts count + cnx count + vbs count + points + conics + verbs.
			var pts = r.ReadInt32();
			var cnx = r.ReadInt32();
			var vbs = r.ReadInt32();
			var points = new float[pts * 2];
			for (var i = 0; i < points.Length; i++) points[i] = r.ReadSingle();
			var conics = new float[cnx];
			for (var i = 0; i < cnx; i++) conics[i] = r.ReadSingle();
			var verbs = r.ReadBytes(vbs);
			// If we're on the old kJustPublicData_Version (=4), verbs were
			// stored in reverse. Flip them so downstream emit is uniform.
			if (version == 4) Array.Reverse(verbs);
			result = new ParsedGeneralPath(fillType, points, conics, verbs);
		}
		else
		{
			throw new InvalidDataException($"unknown path serialization type {serType}");
		}
		// Align read position to 4 bytes (SkWriter32::padToAlign4)
		var pad = (int)((4 - (r.BaseStream.Position - start) % 4) % 4);
		if (pad != 0) r.BaseStream.Position += pad;
		return result;
	}

	// ────────────────────────────────────────────────────────────────────────
	// Image decoder — mirrors SkReadBuffer::readImage in SkReadBuffer.cpp
	// ────────────────────────────────────────────────────────────────────────

	private static ParsedImage DecodeImage(BinaryReader r)
	{
		var flags = r.ReadUInt32();
		var unpremul = (flags & 0x2) != 0;
		var hasMipmap = (flags & 0x1) != 0;
		// hasSubset flag (0x4) isn't written by new .skp files (per skia) but
		// we still handle the case where it appears.
		var hasSubset = (flags & 0x4) != 0;
		var encoded = ReadByteArray(r);

		if (hasSubset)
		{
			// 4 int32s: subset rect. Discard.
			r.BaseStream.Position += 16;
		}

		if (hasMipmap)
		{
			// Discard mip payload.
			ReadByteArray(r);
		}
		return new ParsedImage(encoded, unpremul);
	}

	// ────────────────────────────────────────────────────────────────────────
	// Text-blob decoder — mirrors SkTextBlobPriv::MakeFromBuffer.
	// ────────────────────────────────────────────────────────────────────────

	private static ParsedTextBlob DecodeTextBlob(BinaryReader r)
	{
		var bl = r.ReadSingle();
		var bt = r.ReadSingle();
		var br = r.ReadSingle();
		var bb = r.ReadSingle();
		var runs = new List<ParsedTextRun>();
		while (true)
		{
			var glyphCount = r.ReadInt32();
			if (glyphCount == 0) break;

			var pe = r.ReadUInt32();
			var positioning = (int)(pe & 0x3);
			var extended = (pe & 0x4) != 0;
			var textSize = extended ? r.ReadInt32() : 0;
			var offX = r.ReadSingle();
			var offY = r.ReadSingle();
			var font = DecodeFont(r);

			var glyphs = new ushort[glyphCount];
			// glyphs live inside a length-prefixed byte array (glyphCount * 2 bytes)
			var glyphBytes = ReadByteArray(r);
			Buffer.BlockCopy(glyphBytes, 0, glyphs, 0, Math.Min(glyphBytes.Length, glyphs.Length * 2));

			var scalarsPerGlyph = positioning switch
			{
				0 => 0, // default: just uses offset
				1 => 1, // horizontal: one X per glyph
				2 => 2, // full: X+Y per glyph
				3 => 4, // RSXform: sx, sy, tx, ty per glyph
				_ => throw new InvalidDataException($"bad positioning {positioning}"),
			};
			var positions = new float[glyphCount * scalarsPerGlyph];
			var posBytes = ReadByteArray(r);
			if (positions.Length > 0)
				Buffer.BlockCopy(posBytes, 0, positions, 0, Math.Min(posBytes.Length, positions.Length * 4));

			if (extended)
			{
				// Discard cluster+text byte arrays.
				ReadByteArray(r);
				ReadByteArray(r);
			}

			runs.Add(new ParsedTextRun(glyphCount, positioning, offX, offY, font, glyphs, positions));
		}
		return new ParsedTextBlob(bl, bt, br, bb, runs);
	}

	// SkFont serialization — packed header (size + flags + typeface presence),
	// then optional scalars/typeface index. Mirrors SkFontPriv::Unflatten.
	private static ParsedFont DecodeFont(BinaryReader r)
	{
		var packed = r.ReadUInt32();
		var sizeIsByte = (packed & 0x80000000) != 0;
		var hasScaleX = (packed & 0x40000000) != 0;
		var hasSkewX = (packed & 0x20000000) != 0;
		var hasTypeface = (packed & 0x10000000) != 0;

		var size = sizeIsByte ? (float)((packed >> 16) & 0xFF) : r.ReadSingle();
		var scaleX = hasScaleX ? r.ReadSingle() : 1f;
		var skewX = hasSkewX ? r.ReadSingle() : 0f;
		int typefaceIndex = 0;
		if (hasTypeface)
		{
			// writeTypeface: 0 = null, >0 = index (1-based), <0 = custom (with data).
			// For our purposes: capture index; treat custom as "no known typeface".
			var idx = r.ReadInt32();
			if (idx > 0) typefaceIndex = idx;
			else if (idx < 0)
			{
				// custom typeface data follows: |idx| bytes, padded to 4.
				var size2 = -idx;
				var padded = (size2 + 3) & ~3;
				r.BaseStream.Position += padded;
			}
		}
		var flags = (byte)((packed >> 4) & 0xFFF);
		var edging = (byte)((packed >> 2) & 0x3);
		var hinting = (byte)(packed & 0x3);
		return new ParsedFont(size, scaleX, skewX, flags, edging, hinting, typefaceIndex);
	}

	// SkWriteBuffer::writeByteArray = uint32 length + bytes, padded to 4.
	private static byte[] ReadByteArray(BinaryReader r)
	{
		var len = (int)r.ReadUInt32();
		var data = r.ReadBytes(len);
		var pad = (4 - (len & 3)) & 3;
		if (pad != 0) r.BaseStream.Position += pad;
		return data;
	}

	private static ParsedPaint ReadPaint(BinaryReader r, List<string> factoryNames)
	{
		var strokeWidth = r.ReadSingle();
		var strokeMiter = r.ReadSingle();
		var rC = r.ReadSingle();
		var gC = r.ReadSingle();
		var bC = r.ReadSingle();
		var aC = r.ReadSingle();
		var packed = r.ReadUInt32();

		var antiAlias = (packed & 0x01) != 0;
		var dither = (packed & 0x02) != 0;
		var blendByte = (int)((packed >> 8) & 0xFF);
		var strokeCap = (int)((packed >> 16) & 0x3);
		var strokeJoin = (int)((packed >> 18) & 0x3);
		var style = (int)((packed >> 20) & 0x3);
		var flatFlags = (packed >> 24) & 0xFF;
		var hasEffects = (flatFlags & 0x02) != 0;
		var blend = blendByte == 0xFF ? -1 : blendByte;

		var effects = new ParsedFlattenable?[6];
		if (hasEffects)
			for (var i = 0; i < 6; i++)
				effects[i] = ParseFlattenable(r, factoryNames);

		return new ParsedPaint(
			strokeWidth, strokeMiter,
			rC, gC, bC, aC,
			antiAlias, dither,
			blend, strokeCap, strokeJoin, style,
			hasEffects, effects);
	}

	// Read one flattenable from the buffer. Wire format (from
	// SkReadBuffer::readRawFlattenable / SkBinaryWriteBuffer::writeFlattenable):
	//   int32 factoryIndex   (1-based into picture's "fact" table; 0 = null)
	//   uint32 sizeRecorded  (bytes of payload)
	//   payload of size bytes — per-subclass CreateProc
	// Advances the reader past exactly `sizeRecorded` bytes regardless of
	// whether we recognised the subclass, so callers stay in sync on unknowns.
	private static ParsedFlattenable? ParseFlattenable(BinaryReader r, List<string> factoryNames)
	{
		var idx = r.ReadUInt32();
		if (idx == 0)
			return null;
		string name;
		if ((idx & 0xFF) == 0)
			name = $"<dict-idx {idx >> 8}>";
		else
			name = idx >= 1 && idx <= factoryNames.Count
				? factoryNames[(int)idx - 1]
				: $"<factory {idx}>";

		var payloadSize = (int)r.ReadUInt32();
		var startPos = r.BaseStream.Position;
		ParsedFlattenable? result;
		try
		{
			result = name switch
			{
				"SkModeColorFilter" => DecodeModeColorFilter(r),
				"SkBlurImageFilterImpl" => DecodeBlurImageFilter(r, factoryNames),
				"SkColorFilterImageFilterImpl" => DecodeColorFilterImageFilter(r, factoryNames),
				"SkMatrixTransformImageFilter" => DecodeMatrixTransformImageFilter(r, factoryNames),
				"SkComposeImageFilterImpl" => DecodeComposeImageFilter(r, factoryNames),
				_ => new UnknownFlattenable(name, r.ReadBytes(payloadSize)),
			};
		}
		catch
		{
			r.BaseStream.Position = startPos;
			result = new UnknownFlattenable(name, r.ReadBytes(payloadSize));
		}
		// Force alignment to the recorded end, even if the subclass decoder
		// over- or under-read (unknown-version drift).
		var consumed = r.BaseStream.Position - startPos;
		if (consumed != payloadSize)
			r.BaseStream.Position = startPos + payloadSize;
		return result;
	}

	private static ParsedFlattenable DecodeModeColorFilter(BinaryReader r)
	{
		var rC = r.ReadSingle();
		var gC = r.ReadSingle();
		var bC = r.ReadSingle();
		var aC = r.ReadSingle();
		var mode = r.ReadInt32();
		return new ModeColorFilter(rC, gC, bC, aC, mode);
	}

	// Base image-filter header: int32 inputCount, then for each input a
	// bool (uint32) marker followed (if true) by a full flattenable.
	// Modern .skp (>= v103 kRemoveDeprecatedCropRect) omit the old rect+flags.
	private static ParsedFlattenable?[] DecodeImageFilterCommon(BinaryReader r, List<string> factoryNames)
	{
		var count = r.ReadInt32();
		if (count < 0 || count > 32) throw new InvalidDataException($"image filter input count {count}");
		var inputs = new ParsedFlattenable?[count];
		for (var i = 0; i < count; i++)
		{
			var hasInput = r.ReadUInt32() != 0;
			inputs[i] = hasInput ? ParseFlattenable(r, factoryNames) : null;
		}
		return inputs;
	}

	private static ParsedFlattenable DecodeBlurImageFilter(BinaryReader r, List<string> factoryNames)
	{
		var inputs = DecodeImageFilterCommon(r, factoryNames);
		var sigmaX = r.ReadSingle();
		var sigmaY = r.ReadSingle();
		var tileMode = r.ReadInt32();
		return new BlurImageFilter(inputs, sigmaX, sigmaY, tileMode);
	}

	private static ParsedFlattenable DecodeColorFilterImageFilter(BinaryReader r, List<string> factoryNames)
	{
		var inputs = DecodeImageFilterCommon(r, factoryNames);
		var cf = ParseFlattenable(r, factoryNames);
		return new ColorFilterImageFilter(inputs, cf);
	}

	private static ParsedFlattenable DecodeMatrixTransformImageFilter(BinaryReader r, List<string> factoryNames)
	{
		var inputs = DecodeImageFilterCommon(r, factoryNames);
		var m = new float[9];
		for (var i = 0; i < 9; i++) m[i] = r.ReadSingle();
		var s = DecodeSampling(r);
		return new MatrixTransformImageFilter(inputs, m, s);
	}

	private static ParsedFlattenable DecodeComposeImageFilter(BinaryReader r, List<string> factoryNames)
	{
		var inputs = DecodeImageFilterCommon(r, factoryNames);
		return new ComposeImageFilter(inputs);
	}

	// Sampling wire format: uint32 maxAniso, then (if 0) uint32 useCubic; if
	// true → 2 scalars, else 2 uint32s (filter+mipmap). Matches
	// SkWriter32::writeSampling.
	private static ParsedSampling DecodeSampling(BinaryReader r)
	{
		var maxAniso = r.ReadInt32();
		if (maxAniso != 0) return new ParsedSampling(maxAniso, false, 0, 0, 0, 0);
		var useCubic = r.ReadUInt32() != 0;
		if (useCubic)
		{
			var b = r.ReadSingle();
			var c = r.ReadSingle();
			return new ParsedSampling(0, true, b, c, 0, 0);
		}
		var filter = r.ReadInt32();
		var mipmap = r.ReadInt32();
		return new ParsedSampling(0, false, 0, 0, filter, mipmap);
	}

	private static uint ReadPackedUInt(BinaryReader r)
	{
		var b0 = r.ReadByte();
		return b0 switch
		{
			0xFE => r.ReadUInt16(),
			0xFF => r.ReadUInt32(),
			_    => b0,
		};
	}

	// Decode one SkFontDescriptor-encoded typeface, capturing family name,
	// weight/width/slant, and the (optional) embedded font-data blob.
	// Format (from SkFontDescriptor::serialize/Deserialize):
	//   packedUInt styleBits (weight<<16 | width<<8 | slant)
	//   loop: packedUInt id → until id == kSentinel(0xFF)
	//     kFontFamilyName(0x01)/kFullName(0x04)/kPostscriptName(0x06): packedUInt len + `len` bytes (utf8)
	//     kWeight(0x10)/kWidth(0x11)/kSlant(0x12)/kItalic(0x13): 4-byte scalar
	//     kSyntheticBold(0xF6)/kSyntheticOblique(0xF7): no data
	//     kPaletteIndex(0xF8)/kFactoryId(0xFC)/kFontIndex(0xFD): packedUInt
	//     kPaletteEntryOverrides(0xF9): packedUInt count, then count × (packedUInt idx + uint32 color)
	//     kFontVariation(0xFA): packedUInt count, then count × (uint32 axis + 4-byte scalar)
	//   packedUInt fontDataLen + `fontDataLen` bytes (may be 0)
	private static ParsedTypeface DecodeTypeface(BinaryReader r)
	{
		var styleBits = ReadPackedUInt(r);
		var weight = (int)((styleBits >> 16) & 0xFFFF);
		var width = (int)((styleBits >> 8) & 0xFF);
		var slant = (int)(styleBits & 0xFF);
		string family = "";
		while (true)
		{
			var id = ReadPackedUInt(r);
			if (id == 0xFF) break; // kSentinel
			switch (id)
			{
				case 0x01: // kFontFamilyName
				{
					var len = (int)ReadPackedUInt(r);
					family = Encoding.UTF8.GetString(r.ReadBytes(len));
					break;
				}
				case 0x04: case 0x06: // kFullName, kPostscriptName — read + discard
				{
					var len = (int)ReadPackedUInt(r);
					r.BaseStream.Position += len;
					break;
				}
				case 0x10: case 0x11: case 0x12: case 0x13: // scalar fields
					r.BaseStream.Position += 4;
					break;
				case 0xF6: case 0xF7: // synthetic bold / oblique — no payload
					break;
				case 0xF8: case 0xFC: case 0xFD: // palette index / factory id / font index
					ReadPackedUInt(r);
					break;
				case 0xF9: // palette entry overrides
				{
					var count = ReadPackedUInt(r);
					for (uint i = 0; i < count; i++)
					{
						ReadPackedUInt(r); // index
						r.BaseStream.Position += 4; // color (uint32)
					}
					break;
				}
				case 0xFA: // font variation
				{
					var count = ReadPackedUInt(r);
					for (uint i = 0; i < count; i++)
					{
						r.BaseStream.Position += 4; // axis (uint32)
						r.BaseStream.Position += 4; // value (scalar)
					}
					break;
				}
				default:
					throw new InvalidDataException($"unknown font-descriptor field id 0x{id:X}");
			}
		}
		var fontDataLen = ReadPackedUInt(r);
		byte[]? data = null;
		if (fontDataLen > 0)
			data = r.ReadBytes((int)fontDataLen);
		return new ParsedTypeface(family, weight, width, slant, data);
	}

	private static string ReadBufferString(BinaryReader r)
	{
		// SkWriteBuffer::writeString: length (uint32), then bytes, null-terminated,
		// padded to 4-byte alignment.
		var len = r.ReadUInt32();
		var bytes = r.ReadBytes((int)len);
		// null terminator + padding to 4-byte alignment
		var padded = (int)((len + 4) & ~3u);
		var padding = padded - (int)len;
		r.BaseStream.Position += padding;
		return Encoding.UTF8.GetString(bytes);
	}

	private static void SkipRestOfTagBySeekingToEofOrKnownTag(Stream s)
	{
		s.Position = s.Length;
	}

	private static string ReadTag(BinaryReader r)
	{
		var b = r.ReadBytes(4);
		return new string(new[] { (char)b[3], (char)b[2], (char)b[1], (char)b[0] });
	}

	// ────────────────────────────────────────────────────────────────────────
	// Emitting
	// ────────────────────────────────────────────────────────────────────────

	private static string Emit(ParsedSkp p, string className)
	{
		var sb = new StringBuilder();

		sb.AppendLine("// Auto-generated by SkpDecompiler. Reproduces the drawing captured in an");
		sb.AppendLine("// .skp file via SKCanvas calls.");
		sb.AppendLine("using System;");
		sb.AppendLine("using SkiaSharp;");
		sb.AppendLine("using SkiaSharp.Tests.Visual;");
		sb.AppendLine();
		sb.AppendLine("namespace SkiaSharp.Benchmarks.Rendering.Scenes;");
		sb.AppendLine();
		sb.AppendLine($"public sealed class {className} : ISkiaScene");
		sb.AppendLine("{");

		var origW = p.CullRight - p.CullLeft;
		var origH = p.CullBottom - p.CullTop;
		var infoW = origW is > 0 and < 8192 ? (int)Math.Ceiling(origW) : 1024;
		var infoH = origH is > 0 and < 8192 ? (int)Math.Ceiling(origH) : 1024;

		sb.AppendLine($"\tpublic string Name => \"{className}\";");
		sb.AppendLine($"\t// Original cull: ({p.CullLeft}, {p.CullTop}, {p.CullRight}, {p.CullBottom}) — snapped to {infoW}x{infoH} for benchmarking.");
		sb.AppendLine($"\tpublic SKImageInfo Info => new({infoW}, {infoH}, SKColorType.Rgba8888, SKAlphaType.Premul);");
		sb.AppendLine();

		// Emit resource table summary
		sb.AppendLine("\t// ── Resources ──");
		sb.AppendLine($"\t//   factory names : {p.FactoryNames.Count}");
		sb.AppendLine($"\t//   paints        : {p.Paints.Count}   (fully decoded — see paintTable below)");
		sb.AppendLine($"\t//   paths         : {p.PathCount}   (not decoded — SkPath binary format)");
		sb.AppendLine($"\t//   text blobs    : {p.TextBlobCount}   (not decoded — SkTextBlob binary format)");
		sb.AppendLine($"\t//   vertices      : {p.VerticesCount}   (not decoded)");
		sb.AppendLine($"\t//   images        : {p.ImageCount}   (not decoded — encoded PNG/JPEG bytes)");
		sb.AppendLine($"\t//   sub-pictures  : {p.SubPictureCount}   (not decoded — recursive .skp)");
		sb.AppendLine();

		// Emit factory-name table so effect annotations are legible
		if (p.FactoryNames.Count > 0)
		{
			sb.AppendLine("\t// Factory-name table (flattenable subclass names, referenced by index");
			sb.AppendLine("\t// from paint effects). Useful when eyeballing what kind of shader /");
			sb.AppendLine("\t// filter each paint uses:");
			for (var i = 0; i < p.FactoryNames.Count; i++)
				sb.AppendLine($"\t//   [{i + 1,3}] {p.FactoryNames[i]}");
			sb.AppendLine();
		}

		// Emit paint table
		sb.AppendLine("\tprivate static readonly SKPaint[] paintTable = BuildPaints();");
		sb.AppendLine("\tprivate static SKPaint[] BuildPaints()");
		sb.AppendLine("\t{");
		sb.AppendLine($"\t\tvar paints = new SKPaint[{p.Paints.Count + 1}];");
		sb.AppendLine("\t\t// Index 0 is null in the picture format; keep it as a default paint.");
		sb.AppendLine("\t\tpaints[0] = new SKPaint();");
		for (var i = 0; i < p.Paints.Count; i++)
			EmitPaint(sb, i + 1, p.Paints[i]);
		sb.AppendLine("\t\treturn paints;");
		sb.AppendLine("\t}");
		sb.AppendLine();

		// Typefaces are shared across the whole picture tree; other resource
		// tables (paths, images, text blobs) are scoped to each picture.
		EmitTypefaceTable(sb, p, "\t");
		EmitPathTable(sb, p, "\t");
		EmitImageTable(sb, p, "\t");
		EmitTextBlobTable(sb, p, "\t", className);

		sb.AppendLine("\tpublic void Draw(SKCanvas canvas)");
		sb.AppendLine("\t{");
		AppendOps(sb, DecodeOps(p));
		sb.AppendLine("\t}");
		sb.AppendLine();

		EmitSubPictureClasses(sb, p, className);

		sb.AppendLine("}");
		sb.AppendLine();
		sb.AppendLine($"// -- header --");
		sb.AppendLine($"// version = {p.Version}");
		sb.AppendLine($"// op stream = {p.OpData.Length} bytes; array buffer = {p.ArrayBufferBytes} bytes");
		AppendPictureTreeSummary(sb, p);
		return sb.ToString();
	}

	// Walks the picture tree in pre-order and emits a nested private static
	// class for every non-top-level ParsedSkp. Each class carries its own
	// paint/path/image/textBlob tables so DecodeOps' emitted `paintTable[i]`
	// references resolve to the sub-picture's own indices. Typefaces are
	// shared from the top-level class via `topClassName.typefaceTable`.
	private static void EmitSubPictureClasses(StringBuilder sb, ParsedSkp top, string topClassName)
	{
		var all = new List<ParsedSkp>();
		FlattenTree(top, all);
		foreach (var sub in all)
		{
			if (sub.GlobalId == 0) continue; // top-level is the enclosing class
			EmitOneSubPicture(sb, sub, topClassName);
		}
	}

	private static void FlattenTree(ParsedSkp p, List<ParsedSkp> flat)
	{
		flat.Add(p);
		foreach (var sub in p.SubPictures)
			FlattenTree(sub, flat);
	}

	private static void EmitOneSubPicture(StringBuilder sb, ParsedSkp sub, string topClassName)
	{
		sb.AppendLine($"\t// Sub-picture {sub.GlobalId} — cull ({sub.CullLeft}, {sub.CullTop}, {sub.CullRight}, {sub.CullBottom}); {sub.OpData.Length} op bytes; {sub.Paints.Count} paints; {sub.Paths.Count} paths; {sub.Images.Count} images; {sub.TextBlobs.Count} textblobs; {sub.SubPictures.Count} nested sub-pictures.");
		sb.AppendLine($"\tprivate static class SubPic_{sub.GlobalId}");
		sb.AppendLine("\t{");

		if (sub.Unparseable)
		{
			sb.AppendLine($"\t\t// Unparseable: {sub.BailReason}. Draw() is a no-op — the visual output will be missing this sub-tree.");
			sb.AppendLine("\t\tpublic static void Draw(SKCanvas canvas) { }");
			sb.AppendLine("\t}");
			sb.AppendLine();
			return;
		}

		// Own resource tables — DecodeOps' emitted `paintTable[i]` etc. bind
		// to whichever class encloses the Draw method.
		sb.AppendLine($"\t\tprivate static readonly SKPaint[] paintTable = BuildPaints();");
		sb.AppendLine("\t\tprivate static SKPaint[] BuildPaints()");
		sb.AppendLine("\t\t{");
		sb.AppendLine($"\t\t\tvar paints = new SKPaint[{sub.Paints.Count + 1}];");
		sb.AppendLine("\t\t\tpaints[0] = new SKPaint();");
		for (var i = 0; i < sub.Paints.Count; i++)
			EmitPaint(sb, i + 1, sub.Paints[i], indent: "\t\t\t");
		sb.AppendLine("\t\t\treturn paints;");
		sb.AppendLine("\t\t}");
		EmitPathTable(sb, sub, "\t\t");
		EmitImageTable(sb, sub, "\t\t");
		EmitTextBlobTable(sb, sub, "\t\t", topClassName);

		sb.AppendLine("\t\tpublic static void Draw(SKCanvas canvas)");
		sb.AppendLine("\t\t{");
		AppendOps(sb, DecodeOps(sub), extraIndent: "\t");
		sb.AppendLine("\t\t}");

		sb.AppendLine("\t}");
		sb.AppendLine();
	}

	private static void AppendPictureTreeSummary(StringBuilder sb, ParsedSkp top)
	{
		var all = new List<ParsedSkp>();
		FlattenTree(top, all);
		sb.AppendLine($"// tree: {all.Count} picture(s) total; {all.Count(x => x.Unparseable)} unparseable");
		if (top.Typefaces.Count > 0)
		{
			var totalFontData = top.Typefaces.Sum(t => t.FontData?.Length ?? 0);
			sb.AppendLine($"// typefaces: {top.Typefaces.Count} at top level; total embedded data {totalFontData:N0} bytes");
			for (var i = 0; i < top.Typefaces.Count; i++)
			{
				var t = top.Typefaces[i];
				sb.AppendLine($"//   [{i + 1}] '{t.FamilyName}' w={t.Weight} width={t.Width} slant={t.SlantEnum} data={(t.FontData?.Length ?? 0):N0} bytes");
			}
		}
		foreach (var p in all)
			sb.AppendLine($"//   SubPic_{p.GlobalId}: {p.OpData.Length} op bytes, {p.Paths.Count} paths, {p.Images.Count} images, {p.TextBlobs.Count} textblobs, {p.SubPictures.Count} child(ren){(p.Unparseable ? $" [UNPARSEABLE: {p.BailReason}]" : "")}");
	}

	// Splits a raw op stream into four segments so each partition can be
	// rendered independently on its own canvas without corrupting the
	// destination.
	//
	// contentPrologue = leading content ops at depth 0 (e.g. DrawPaint bg).
	//                   Executed once, only in partition 0. Duplicating it
	//                   into every partition would paint over each other on
	//                   parallel-recorder inserts.
	// statePrologue   = state-only ops immediately after contentPrologue that
	//                   raise the save-stack to the body's baseline depth
	//                   (e.g. Save + SetMatrix). Idempotent — every
	//                   partition replays it to enter the body's ambient
	//                   canvas state.
	// body            = the actual scene draws, all rooted at
	//                   baselineDepth = SaveDepthAfter of last statePrologue op.
	// stateEpilogue   = trailing state-only ops that pop the save-stack
	//                   back down to 0 (matching Restores). Every partition
	//                   replays it to leave its canvas balanced.
	private static (List<OpEmission> contentPrologue, List<OpEmission> statePrologue, List<OpEmission> body, List<OpEmission> stateEpilogue, int baselineDepth)
		SegmentOps(List<OpEmission> ops)
	{
		if (ops.Count == 0)
			return (new(), new(), new(), new(), 0);

		// Anchor everything on the first op whose SaveDepthAfter > 0 — that's
		// where the outer wrap begins. Ops before it are pure depth-0 content
		// (background clear etc.). If no op ever leaves depth 0, the picture
		// has no outer wrap and everything is straight body.
		int firstSaveIdx = -1;
		for (var i = 0; i < ops.Count; i++)
			if (ops[i].SaveDepthAfter > 0) { firstSaveIdx = i; break; }

		if (firstSaveIdx < 0)
			return (new(), new(), new List<OpEmission>(ops), new(), 0);

		int contentEnd = firstSaveIdx;

		int stateEnd = contentEnd;
		while (stateEnd < ops.Count && ops[stateEnd].IsState)
			stateEnd++;

		int epilogueStart = ops.Count;
		while (epilogueStart > stateEnd && ops[epilogueStart - 1].IsState)
			epilogueStart--;

		var contentPrologue = ops.GetRange(0, contentEnd);
		var statePrologue = ops.GetRange(contentEnd, stateEnd - contentEnd);
		var body = ops.GetRange(stateEnd, Math.Max(0, epilogueStart - stateEnd));
		var stateEpilogue = ops.GetRange(epilogueStart, ops.Count - epilogueStart);

		var baselineDepth = statePrologue.Count > 0
			? statePrologue[statePrologue.Count - 1].SaveDepthAfter
			: 0;

		return (contentPrologue, statePrologue, body, stateEpilogue, baselineDepth);
	}

	// Split body ops into partitionCount buckets aiming for equal size.
	// Split points must be at baselineDepth (delta-0 boundaries) so each
	// bucket is Save-balanced. We first collect ALL candidate boundaries,
	// then pick partitionCount-1 of them closest to evenly-spaced targets.
	// If fewer natural balanced points exist than requested, returns fewer
	// buckets — never fabricates unbalanced ones.
	private static List<List<OpEmission>> PartitionBody(List<OpEmission> body, int partitionCount, int baselineDepth)
	{
		var buckets = new List<List<OpEmission>>();
		if (partitionCount <= 1 || body.Count == 0)
		{
			buckets.Add(new List<OpEmission>(body));
			return buckets;
		}

		// Candidate indices: ops whose SaveDepthAfter is exactly baselineDepth.
		// Exclude the very last op — everything up to and including it is
		// unconditionally part of the final bucket.
		var rawCandidates = new List<int>();
		for (var i = 0; i < body.Count - 1; i++)
			if (body[i].SaveDepthAfter == baselineDepth)
				rawCandidates.Add(i);

		if (rawCandidates.Count == 0)
		{
			buckets.Add(new List<OpEmission>(body));
			return buckets;
		}

		// Coalesce nearby candidates so we don't emit tiny 1-op buckets when
		// a dense cluster of balanced-depth ops (e.g. many DRAW_PICTURE at
		// baseline) sit right next to each other. Keep the first candidate
		// of each cluster, and require subsequent candidates to be at least
		// minSpacing ops past the last kept one — minSpacing scales with
		// the ideal per-bucket size so wide streams still find good splits.
		var minSpacing = Math.Max(1, body.Count / partitionCount);
		var candidates = new List<int>();
		foreach (var c in rawCandidates)
			if (candidates.Count == 0 || c - candidates[candidates.Count - 1] >= minSpacing)
				candidates.Add(c);

		var wantSplits = Math.Min(partitionCount - 1, candidates.Count);
		var chosen = new HashSet<int>();
		for (var k = 1; k <= wantSplits; k++)
		{
			var idx = (int)((long)k * candidates.Count / (wantSplits + 1));
			if (idx >= candidates.Count) idx = candidates.Count - 1;
			chosen.Add(candidates[idx]);
		}

		var ordered = new List<int>(chosen);
		ordered.Sort();

		var start = 0;
		foreach (var split in ordered)
		{
			buckets.Add(body.GetRange(start, split - start + 1));
			start = split + 1;
		}
		if (start < body.Count)
			buckets.Add(body.GetRange(start, body.Count - start));
		return buckets;
	}

	private static string EmitPartitioned(ParsedSkp p, string className, int partitionCount)
	{
		var ops = DecodeOps(p);
		var (contentPrologue, statePrologue, body, stateEpilogue, baselineDepth) = SegmentOps(ops);
		var buckets = PartitionBody(body, partitionCount, baselineDepth);
		var actual = buckets.Count;

		var sb = new StringBuilder();

		sb.AppendLine("// Auto-generated by SkpDecompiler --partition. Reproduces the drawing");
		sb.AppendLine("// captured in an .skp file, split into N methods that can be recorded");
		sb.AppendLine("// in parallel on separate Graphite Recorders.");
		sb.AppendLine("using System;");
		sb.AppendLine("using SkiaSharp;");
		sb.AppendLine("using SkiaSharp.Benchmarks.Rendering;");
		sb.AppendLine("using SkiaSharp.Tests.Visual;");
		sb.AppendLine();
		sb.AppendLine("namespace SkiaSharp.Benchmarks.Rendering.Scenes;");
		sb.AppendLine();
		sb.AppendLine($"public sealed class {className} : IPartitionedSkiaScene");
		sb.AppendLine("{");

		var origW = p.CullRight - p.CullLeft;
		var origH = p.CullBottom - p.CullTop;
		var infoW = origW is > 0 and < 8192 ? (int)Math.Ceiling(origW) : 1024;
		var infoH = origH is > 0 and < 8192 ? (int)Math.Ceiling(origH) : 1024;

		sb.AppendLine($"\tpublic string Name => \"{className}\";");
		sb.AppendLine($"\t// Original cull: ({p.CullLeft}, {p.CullTop}, {p.CullRight}, {p.CullBottom}) — snapped to {infoW}x{infoH} for benchmarking.");
		sb.AppendLine($"\tpublic SKImageInfo Info => new({infoW}, {infoH}, SKColorType.Rgba8888, SKAlphaType.Premul);");
		sb.AppendLine($"\tpublic int PartitionCount => {actual};");
		if (actual != partitionCount)
			sb.AppendLine($"\t// NB: {partitionCount} partitions requested but only {actual} balanced-Save/Restore boundaries were available.");
		sb.AppendLine();

		sb.AppendLine("\t// ── Resources ──");
		sb.AppendLine($"\t//   factory names : {p.FactoryNames.Count}");
		sb.AppendLine($"\t//   paints        : {p.Paints.Count}   (fully decoded — see paintTable below)");
		sb.AppendLine($"\t//   paths         : {p.PathCount}   (not decoded — SkPath binary format)");
		sb.AppendLine($"\t//   text blobs    : {p.TextBlobCount}   (not decoded — SkTextBlob binary format)");
		sb.AppendLine($"\t//   vertices      : {p.VerticesCount}   (not decoded)");
		sb.AppendLine($"\t//   images        : {p.ImageCount}   (not decoded — encoded PNG/JPEG bytes)");
		sb.AppendLine($"\t//   sub-pictures  : {p.SubPictureCount}   (not decoded — recursive .skp)");
		sb.AppendLine();

		if (p.FactoryNames.Count > 0)
		{
			sb.AppendLine("\t// Factory-name table:");
			for (var i = 0; i < p.FactoryNames.Count; i++)
				sb.AppendLine($"\t//   [{i + 1,3}] {p.FactoryNames[i]}");
			sb.AppendLine();
		}

		sb.AppendLine("\tprivate static readonly SKPaint[] paintTable = BuildPaints();");
		sb.AppendLine("\tprivate static SKPaint[] BuildPaints()");
		sb.AppendLine("\t{");
		sb.AppendLine($"\t\tvar paints = new SKPaint[{p.Paints.Count + 1}];");
		sb.AppendLine("\t\tpaints[0] = new SKPaint();");
		for (var i = 0; i < p.Paints.Count; i++)
			EmitPaint(sb, i + 1, p.Paints[i]);
		sb.AppendLine("\t\treturn paints;");
		sb.AppendLine("\t}");
		sb.AppendLine();

		EmitTypefaceTable(sb, p, "\t");
		EmitPathTable(sb, p, "\t");
		EmitImageTable(sb, p, "\t");
		EmitTextBlobTable(sb, p, "\t", className);

		// Sequential Draw drives all partitions in order (identical to
		// running each DrawPartition_i on the same canvas). This is what
		// the sequential Ganesh/Graphite/raster backends will execute.
		sb.AppendLine("\tpublic void Draw(SKCanvas canvas)");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tfor (var i = 0; i < PartitionCount; i++)");
		sb.AppendLine("\t\t\tDrawPartition(canvas, i);");
		sb.AppendLine("\t}");
		sb.AppendLine();

		sb.AppendLine("\tpublic void DrawPartition(SKCanvas canvas, int partitionIndex)");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tswitch (partitionIndex)");
		sb.AppendLine("\t\t{");
		for (var i = 0; i < actual; i++)
			sb.AppendLine($"\t\t\tcase {i}: DrawPartition{i}(canvas); return;");
		sb.AppendLine("\t\t\tdefault: throw new System.ArgumentOutOfRangeException(nameof(partitionIndex));");
		sb.AppendLine("\t\t}");
		sb.AppendLine("\t}");
		sb.AppendLine();

		// Shared ContentPrologue: only executed by partition 0. Contains any
		// initial drawing ops (typically a DrawPaint background clear) that
		// were emitted at save-depth 0 before the outer wrap started.
		sb.AppendLine($"\t// Content prologue — {contentPrologue.Count} op(s), depth-0 drawing ops that");
		sb.AppendLine("\t// only partition 0 replays (duplicating in other partitions would over-paint");
		sb.AppendLine("\t// them on parallel inserts). Executed once per frame.");
		sb.AppendLine("\tprivate static void ContentPrologue(SKCanvas canvas)");
		sb.AppendLine("\t{");
		AppendOps(sb, contentPrologue);
		sb.AppendLine("\t}");
		sb.AppendLine();

		// Shared StatePrologue: replayed by every partition to bring its
		// canvas from depth 0 → baselineDepth with matching matrix/clip
		// state. Idempotent — safe to run multiple times on the same canvas.
		sb.AppendLine($"\t// State prologue — {statePrologue.Count} op(s), state-only setup that raises");
		sb.AppendLine($"\t// the save-stack to baseline depth {baselineDepth} and establishes matrix/clip.");
		sb.AppendLine("\t// Replayed by every partition, so parallel recorders enter body at identical state.");
		sb.AppendLine("\tprivate static void StatePrologue(SKCanvas canvas)");
		sb.AppendLine("\t{");
		AppendOps(sb, statePrologue);
		sb.AppendLine("\t}");
		sb.AppendLine();

		// Shared StateEpilogue: closing Restores that unwind statePrologue's
		// saves. Replayed by every partition to leave its canvas balanced.
		sb.AppendLine($"\t// State epilogue — {stateEpilogue.Count} op(s), closing Restores that unwind");
		sb.AppendLine("\t// the state prologue and return the canvas to depth 0.");
		sb.AppendLine("\tprivate static void StateEpilogue(SKCanvas canvas)");
		sb.AppendLine("\t{");
		AppendOps(sb, stateEpilogue);
		sb.AppendLine("\t}");
		sb.AppendLine();

		for (var i = 0; i < actual; i++)
		{
			sb.AppendLine($"\t// Partition {i}: {buckets[i].Count} body op(s)");
			sb.AppendLine($"\tprivate static void DrawPartition{i}(SKCanvas canvas)");
			sb.AppendLine("\t{");
			if (i == 0)
				sb.AppendLine("\t\tContentPrologue(canvas);");
			sb.AppendLine("\t\tStatePrologue(canvas);");
			AppendOps(sb, buckets[i]);
			sb.AppendLine("\t\tStateEpilogue(canvas);");
			sb.AppendLine("\t}");
			sb.AppendLine();
		}

		EmitSubPictureClasses(sb, p, className);

		sb.AppendLine("}");
		sb.AppendLine();
		sb.AppendLine($"// -- header --");
		sb.AppendLine($"// version = {p.Version}");
		sb.AppendLine($"// op stream = {p.OpData.Length} bytes; array buffer = {p.ArrayBufferBytes} bytes");
		sb.AppendLine($"// requested partitions = {partitionCount}; actual = {actual}");
		for (var i = 0; i < actual; i++)
			sb.AppendLine($"// partition {i}: {buckets[i].Count} ops");
		AppendPictureTreeSummary(sb, p);
		return sb.ToString();
	}

	private static void EmitPaint(StringBuilder sb, int idx, ParsedPaint pt, string indent = "\t\t")
	{
		sb.Append($"{indent}paints[{idx}] = new SKPaint {{ ");
		sb.Append($"Color = new SKColor({B255(pt.R)}, {B255(pt.G)}, {B255(pt.B)}, {B255(pt.A)})");
		if (pt.AntiAlias) sb.Append(", IsAntialias = true");
		if (pt.Dither) sb.Append(", IsDither = true");
		if (pt.Style != 0) sb.Append($", Style = (SKPaintStyle){pt.Style} /* {StyleName(pt.Style)} */");
		if (pt.StrokeWidth != 0) sb.Append($", StrokeWidth = {F(pt.StrokeWidth)}");
		if (pt.StrokeMiter != 4) sb.Append($", StrokeMiter = {F(pt.StrokeMiter)}");
		if (pt.StrokeCap != 0) sb.Append($", StrokeCap = (SKStrokeCap){pt.StrokeCap}");
		if (pt.StrokeJoin != 0) sb.Append($", StrokeJoin = (SKStrokeJoin){pt.StrokeJoin}");
		if (pt.BlendMode >= 0 && pt.BlendMode != (int)SKBlendMode.SrcOver)
			sb.Append($", BlendMode = (SKBlendMode){pt.BlendMode} /* {BlendName(pt.BlendMode)} */");
		sb.AppendLine(" };");

		if (!pt.HasEffects) return;

		// Paint effect slots: 0=PathEffect 1=Shader 2=MaskFilter 3=ColorFilter
		//                    4=ImageFilter 5=Blender. We reconstruct
		// ColorFilter and ImageFilter from their flattenable payloads;
		// PathEffect/Shader/MaskFilter/Blender still fall through to a
		// comment since their subclass formats aren't decoded yet.
		var slotNames = new[] { "PathEffect", "Shader", "MaskFilter", "ColorFilter", "ImageFilter", "Blender" };
		for (var slot = 0; slot < 6; slot++)
		{
			var f = pt.Effects[slot];
			if (f is null) continue;

			var expr = slot switch
			{
				3 => EmitColorFilterExpr(f),
				4 => EmitImageFilterExpr(f),
				_ => null,
			};

			if (expr != null && slotNames[slot] switch
				{
					"ColorFilter" => true,
					"ImageFilter" => true,
					_ => false,
				})
			{
				sb.AppendLine($"{indent}paints[{idx}].{slotNames[slot]} = {expr};");
			}
			else
			{
				sb.AppendLine($"{indent}// paints[{idx}].{slotNames[slot]} = {f.FactoryName} — not reconstructed");
			}
		}
	}

	// ────────────────────────────────────────────────────────────────────────
	// Emit-side flattenable reconstructors — turn ParsedFlattenable back into
	// a SkiaSharp expression at scene-construction time.
	// ────────────────────────────────────────────────────────────────────────

	private static string? EmitColorFilterExpr(ParsedFlattenable f) => f switch
	{
		ModeColorFilter m =>
			$"SKColorFilter.CreateBlendMode(new SKColor({B255(m.R)}, {B255(m.G)}, {B255(m.B)}, {B255(m.A)}), (SKBlendMode){m.BlendMode})",
		_ => null,
	};

	private static string? EmitImageFilterExpr(ParsedFlattenable f)
	{
		switch (f)
		{
			case BlurImageFilter blur:
			{
				var input = blur.Inputs.Length > 0 ? EmitImageFilterExpr(blur.Inputs[0]) : null;
				return $"SKImageFilter.CreateBlur({F(blur.SigmaX)}, {F(blur.SigmaY)}, (SKShaderTileMode){blur.TileMode}, {input ?? "null"})";
			}
			case ColorFilterImageFilter cf:
			{
				var input = cf.Inputs.Length > 0 ? EmitImageFilterExpr(cf.Inputs[0]) : null;
				var inner = cf.ColorFilter is null ? "null" : EmitColorFilterExpr(cf.ColorFilter);
				return $"SKImageFilter.CreateColorFilter({inner ?? "null"}, {input ?? "null"})";
			}
			case MatrixTransformImageFilter mt:
			{
				var input = mt.Inputs.Length > 0 ? EmitImageFilterExpr(mt.Inputs[0]) : null;
				var m = mt.Matrix9;
				var matrix = $"new SKMatrix({F(m[0])}, {F(m[1])}, {F(m[2])}, {F(m[3])}, {F(m[4])}, {F(m[5])}, {F(m[6])}, {F(m[7])}, {F(m[8])})";
				return $"SKImageFilter.CreateMatrix({matrix}, {EmitSamplingExpr(mt.Sampling)}, {input ?? "null"})";
			}
			case ComposeImageFilter co:
			{
				var outer = co.Inputs.Length > 0 ? EmitImageFilterExpr(co.Inputs[0]) : null;
				var inner = co.Inputs.Length > 1 ? EmitImageFilterExpr(co.Inputs[1]) : null;
				return $"SKImageFilter.CreateCompose({outer ?? "null"}, {inner ?? "null"})";
			}
			case UnknownFlattenable u:
				return $"/* unknown image filter '{Escape(u.FactoryName)}' */ null";
			case null:
				return null;
			default:
				return null;
		}
	}

	private static string EmitSamplingExpr(ParsedSampling s)
	{
		if (s.MaxAniso != 0)
			return $"new SKSamplingOptions({s.MaxAniso})";
		if (s.UseCubic)
			return $"new SKSamplingOptions(new SKCubicResampler({F(s.CubicB)}, {F(s.CubicC)}))";
		return $"new SKSamplingOptions((SKFilterMode){s.Filter}, (SKMipmapMode){s.Mipmap})";
	}

	// ────────────────────────────────────────────────────────────────────────
	// Table emitters — typefaces, paths, images, text blobs
	//
	// Called with `indent` = whitespace before class-level members. Top-level
	// class uses "\t"; nested SubPic_N classes use "\t\t".
	// ────────────────────────────────────────────────────────────────────────

	// Typefaces live at the top level only. When the .skp embeds font data
	// (Skia's default kDoIncludeData mode), we honour that data by base64-ing
	// it into the emitted source and running it through SKTypeface.FromData
	// at scene-construction time. If no data was present, we fall back to
	// SKFontManager family-name matching. Enum values on the wire:
	// SkFontStyle::Slant → 0=Upright, 1=Italic, 2=Oblique.
	private static void EmitTypefaceTable(StringBuilder sb, ParsedSkp top, string indent)
	{
		var body = indent + "\t";
		sb.AppendLine($"{indent}// Typefaces referenced by every text blob in the tree. Rebuilt from the");
		sb.AppendLine($"{indent}// embedded font data so glyph IDs used by the text blobs resolve to the");
		sb.AppendLine($"{indent}// exact fonts the .skp was captured against, not host-system substitutes.");

		// _fontData_N fields MUST be declared before typefaceTable so that
		// static-field init order runs them first — BuildTypefaces reads
		// them, and C# initializes static fields in source order.
		for (var i = 0; i < top.Typefaces.Count; i++)
		{
			var t = top.Typefaces[i];
			if (t.FontData is not { } data || data.Length == 0)
				continue;
			sb.AppendLine($"{indent}// {data.Length:N0} bytes of embedded font data for typeface {i + 1} ('{Escape(t.FamilyName)}').");
			sb.AppendLine($"{indent}private static readonly string _fontData_{i + 1} =");
			EmitBase64Chunks(sb, indent + "\t\t", data);
			sb.AppendLine($"{indent}\t\t;");
		}

		sb.AppendLine($"{indent}private static readonly SKTypeface[] typefaceTable = BuildTypefaces();");
		sb.AppendLine($"{indent}private static SKTypeface[] BuildTypefaces()");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{body}var tf = new SKTypeface[{top.Typefaces.Count + 1}];");
		sb.AppendLine($"{body}tf[0] = SKTypeface.Default; // index-0 slot; SkFont typeface index is 1-based");
		for (var i = 0; i < top.Typefaces.Count; i++)
		{
			var t = top.Typefaces[i];
			var slant = t.SlantEnum switch { 1 => "Italic", 2 => "Oblique", _ => "Upright" };
			var family = Escape(t.FamilyName);
			if (t.FontData is { } fd && fd.Length > 0)
			{
				sb.AppendLine($"{body}tf[{i + 1}] = SKTypeface.FromData(SKData.CreateCopy(Convert.FromBase64String(_fontData_{i + 1})))");
				sb.AppendLine($"{body}\t?? SKTypeface.FromFamilyName(\"{family}\", {t.Weight}, {t.Width}, SKFontStyleSlant.{slant});");
			}
			else
			{
				sb.AppendLine($"{body}tf[{i + 1}] = SKTypeface.FromFamilyName(\"{family}\", {t.Weight}, {t.Width}, SKFontStyleSlant.{slant});");
			}
		}
		sb.AppendLine($"{body}return tf;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	// Chunk base64 so the compiler doesn't have to swallow a multi-megabyte
	// literal in one gulp — each line is a ~120-char string joined by `+`.
	private static void EmitBase64Chunks(StringBuilder sb, string indent, byte[] data)
	{
		const int chunkCharCount = 120;
		var b64 = Convert.ToBase64String(data);
		var first = true;
		for (var i = 0; i < b64.Length; i += chunkCharCount)
		{
			var slice = b64.Substring(i, Math.Min(chunkCharCount, b64.Length - i));
			sb.Append(indent);
			sb.Append(first ? "  " : "+ ");
			sb.Append('"');
			sb.Append(slice);
			sb.Append('"');
			sb.AppendLine();
			first = false;
		}
	}

	// pathTable[0] = null so 1-based indices from DRAW_PATH resolve directly.
	private static void EmitPathTable(StringBuilder sb, ParsedSkp p, string indent)
	{
		var body = indent + "\t";
		sb.AppendLine($"{indent}private static readonly SKPath[] pathTable = BuildPaths();");
		sb.AppendLine($"{indent}private static SKPath[] BuildPaths()");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{body}var paths = new SKPath[{p.Paths.Count + 1}];");
		sb.AppendLine($"{body}paths[0] = new SKPath();");
		for (var i = 0; i < p.Paths.Count; i++)
			EmitOnePath(sb, i + 1, p.Paths[i], body);
		sb.AppendLine($"{body}return paths;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	private static void EmitOnePath(StringBuilder sb, int idx, ParsedPath path, string indent)
	{
		var fill = ((SKPathFillType)path.FillType).ToString();
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tvar p = new SKPath {{ FillType = SKPathFillType.{fill} }};");
		if (path is ParsedRRectPath rr)
		{
			// Wire layout: floats 0-3 = rect (l,t,r,b), 4-11 = four (x,y) radii for corners UL,UR,LR,LL.
			var f = rr.Rrect12;
			sb.AppendLine($"{indent}\tvar rrect = new SKRoundRect();");
			sb.AppendLine($"{indent}\trrect.SetRectRadii(new SKRect({F(f[0])}, {F(f[1])}, {F(f[2])}, {F(f[3])}), new[] {{");
			sb.AppendLine($"{indent}\t\tnew SKPoint({F(f[4])}, {F(f[5])}),  // UL");
			sb.AppendLine($"{indent}\t\tnew SKPoint({F(f[6])}, {F(f[7])}),  // UR");
			sb.AppendLine($"{indent}\t\tnew SKPoint({F(f[8])}, {F(f[9])}),  // LR");
			sb.AppendLine($"{indent}\t\tnew SKPoint({F(f[10])}, {F(f[11])}) // LL");
			sb.AppendLine($"{indent}\t}});");
			sb.AppendLine($"{indent}\tp.AddRoundRect(rrect, SKPathDirection.{((SKPathDirection)rr.Direction)});");
		}
		else if (path is ParsedGeneralPath gp)
		{
			// Walk verbs, consuming from points and conics streams.
			int pi = 0, ci = 0;
			foreach (var v in gp.Verbs)
			{
				switch (v)
				{
					case 0: // Move
						sb.AppendLine($"{indent}\tp.MoveTo({F(gp.Points[pi * 2])}, {F(gp.Points[pi * 2 + 1])});");
						pi += 1; break;
					case 1: // Line
						sb.AppendLine($"{indent}\tp.LineTo({F(gp.Points[pi * 2])}, {F(gp.Points[pi * 2 + 1])});");
						pi += 1; break;
					case 2: // Quad
						sb.AppendLine($"{indent}\tp.QuadTo({F(gp.Points[pi * 2])}, {F(gp.Points[pi * 2 + 1])}, {F(gp.Points[pi * 2 + 2])}, {F(gp.Points[pi * 2 + 3])});");
						pi += 2; break;
					case 3: // Conic
						sb.AppendLine($"{indent}\tp.ConicTo({F(gp.Points[pi * 2])}, {F(gp.Points[pi * 2 + 1])}, {F(gp.Points[pi * 2 + 2])}, {F(gp.Points[pi * 2 + 3])}, {F(gp.Conics[ci])});");
						pi += 2; ci += 1; break;
					case 4: // Cubic
						sb.AppendLine($"{indent}\tp.CubicTo({F(gp.Points[pi * 2])}, {F(gp.Points[pi * 2 + 1])}, {F(gp.Points[pi * 2 + 2])}, {F(gp.Points[pi * 2 + 3])}, {F(gp.Points[pi * 2 + 4])}, {F(gp.Points[pi * 2 + 5])});");
						pi += 3; break;
					case 5: // Close
						sb.AppendLine($"{indent}\tp.Close();");
						break;
					default:
						sb.AppendLine($"{indent}\t// unknown verb {v}");
						break;
				}
			}
		}
		sb.AppendLine($"{indent}\tpaths[{idx}] = p;");
		sb.AppendLine($"{indent}}}");
	}

	// imageTable[0] = null placeholder; encoded PNG/JPEG bytes emitted as
	// Base64 constants and decoded via SKImage.FromEncodedData at first use.
	private static void EmitImageTable(StringBuilder sb, ParsedSkp p, string indent)
	{
		var body = indent + "\t";
		sb.AppendLine($"{indent}private static readonly SKImage[] imageTable = BuildImages();");
		sb.AppendLine($"{indent}private static SKImage[] BuildImages()");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{body}var imgs = new SKImage[{p.Images.Count + 1}];");
		sb.AppendLine($"{body}imgs[0] = null!;");
		for (var i = 0; i < p.Images.Count; i++)
		{
			var img = p.Images[i];
			var b64 = Convert.ToBase64String(img.EncodedData);
			sb.AppendLine($"{body}imgs[{i + 1}] = SKImage.FromEncodedData(Convert.FromBase64String(\"{b64}\"));");
		}
		sb.AppendLine($"{body}return imgs;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	// textBlobTable[0] = null placeholder; each blob rebuilt via
	// SKTextBlobBuilder + one AddRun call per run. Typeface index resolves
	// against the top-level scene's typefaceTable via qualified name.
	private static void EmitTextBlobTable(StringBuilder sb, ParsedSkp p, string indent, string topClassName)
	{
		var body = indent + "\t";
		sb.AppendLine($"{indent}private static readonly SKTextBlob[] textBlobTable = BuildTextBlobs();");
		sb.AppendLine($"{indent}private static SKTextBlob[] BuildTextBlobs()");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{body}var tfs = {topClassName}.typefaceTable;");
		sb.AppendLine($"{body}var blobs = new SKTextBlob[{p.TextBlobs.Count + 1}];");
		sb.AppendLine($"{body}blobs[0] = null!;");
		for (var i = 0; i < p.TextBlobs.Count; i++)
			EmitOneTextBlob(sb, i + 1, p.TextBlobs[i], body);
		sb.AppendLine($"{body}return blobs;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	private static void EmitOneTextBlob(StringBuilder sb, int idx, ParsedTextBlob blob, string indent)
	{
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tvar b = new SKTextBlobBuilder();");
		foreach (var run in blob.Runs)
		{
			sb.AppendLine($"{indent}\t{{");
			// Font — SkiaSharp ctor: SKFont(typeface, size, scaleX, skewX).
			sb.AppendLine($"{indent}\t\tvar font = new SKFont(tfs[{run.Font.TypefaceIndex}], {F(run.Font.Size)}, {F(run.Font.ScaleX)}, {F(run.Font.SkewX)});");
			sb.AppendLine($"{indent}\t\tfont.Edging = (SKFontEdging){run.Font.Edging};");
			sb.AppendLine($"{indent}\t\tfont.Hinting = (SKFontHinting){run.Font.Hinting};");
			var glyphs = string.Join(",", run.Glyphs.Select(g => g.ToString()));
			sb.AppendLine($"{indent}\t\tvar glyphs = new ushort[] {{ {glyphs} }};");
			switch (run.Positioning)
			{
				case 0: // default: single origin
					sb.AppendLine($"{indent}\t\tb.AddRun(glyphs, font, new SKPoint({F(run.OffsetX)}, {F(run.OffsetY)}));");
					break;
				case 1: // horizontal: one X per glyph, shared Y = offY
				{
					var xs = string.Join(",", run.Positions.Select(F));
					sb.AppendLine($"{indent}\t\tvar xs = new float[] {{ {xs} }};");
					sb.AppendLine($"{indent}\t\tb.AddHorizontalRun(glyphs, font, xs, {F(run.OffsetY)});");
					break;
				}
				case 2: // full: SKPoint per glyph
				{
					var pts = new StringBuilder();
					for (var g = 0; g < run.GlyphCount; g++)
					{
						if (g > 0) pts.Append(',');
						pts.Append($"new SKPoint({F(run.Positions[g * 2])},{F(run.Positions[g * 2 + 1])})");
					}
					sb.AppendLine($"{indent}\t\tvar pts = new SKPoint[] {{ {pts} }};");
					sb.AppendLine($"{indent}\t\tb.AddPositionedRun(glyphs, font, pts);");
					break;
				}
				case 3: // RSXform: 4 floats per glyph → SKRotationScaleMatrix (ScaleX, Skew, TranslateX, TranslateY).
				{
					var xforms = new StringBuilder();
					for (var g = 0; g < run.GlyphCount; g++)
					{
						if (g > 0) xforms.Append(',');
						xforms.Append($"new SKRotationScaleMatrix({F(run.Positions[g * 4])},{F(run.Positions[g * 4 + 1])},{F(run.Positions[g * 4 + 2])},{F(run.Positions[g * 4 + 3])})");
					}
					sb.AppendLine($"{indent}\t\tvar xforms = new SKRotationScaleMatrix[] {{ {xforms} }};");
					sb.AppendLine($"{indent}\t\tb.AddRotationScaleRun(glyphs, font, xforms);");
					break;
				}
			}
			sb.AppendLine($"{indent}\t}}");
		}
		sb.AppendLine($"{indent}\tblobs[{idx}] = b.Build()!;");
		sb.AppendLine($"{indent}}}");
	}

	private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

	private static byte B255(float f)
	{
		var v = (int)Math.Round(f * 255f);
		return (byte)Math.Clamp(v, 0, 255);
	}

	private static string StyleName(int s) => s switch { 0 => "Fill", 1 => "Stroke", 2 => "StrokeAndFill", _ => $"?{s}" };
	private static string BlendName(int b) => b >= 0 && b <= (int)SKBlendMode.Luminosity ? ((SKBlendMode)b).ToString() : $"?{b}";

	// ────────────────────────────────────────────────────────────────────────
	// Op stream decoder (unchanged from the earlier version; the only reason it
	// stays inside this file is that emitters and parsers share the same
	// StringBuilder to keep call ordering right)
	// ────────────────────────────────────────────────────────────────────────

	// State-only ops don't produce visible output — they only mutate the
	// canvas's matrix, clip, or save-stack. Partitions can replay them as
	// often as needed without changing rendered pixels; drawing ops cannot.
	private static readonly HashSet<string> StateOnlyOps = new()
	{
		"SAVE", "RESTORE", "SAVE_BEHIND",
		"SET_MATRIX", "SET_M44", "CONCAT", "CONCAT44",
		"TRANSLATE", "SCALE", "ROTATE", "SKEW",
		"CLIP_PATH", "CLIP_REGION", "CLIP_RECT", "CLIP_RRECT",
		"CLIP_SHADER_IN_PAINT", "RESET_CLIP",
		"NOOP", "FLUSH",
	};

	// Per-op emission that also carries the save-stack depth AFTER the op
	// executed and whether the op was pure state. Partitions split on
	// depth-return-to-baseline boundaries and are wrapped by replayed state
	// prologue/epilogue to keep parallel recorders on identical state.
	private sealed record OpEmission(string Text, int SaveDepthAfter, bool IsState);

	private static List<OpEmission> DecodeOps(ParsedSkp picture)
	{
		var ops = new List<OpEmission>();
		var opData = picture.OpData;
		using var s = new MemoryStream(opData);
		using var r = new BinaryReader(s);
		var indent = "\t\t";
		int saveDepth = 0;

		while (s.Position < s.Length)
		{
			var sb = new StringBuilder();
			var opStart = s.Position;
			if (s.Length - opStart < 4) break;

			var word = r.ReadUInt32();
			var op = (byte)((word >> 24) & 0xFF);
			var size = (int)(word & 0x00FFFFFF);
			if (size == 0x00FFFFFF)
				size = r.ReadInt32();
			if (size < 4 || opStart + size > opData.Length)
			{
				sb.AppendLine($"{indent}// malformed op at offset {opStart}: op={op}, size={size}. Stopping.");
				ops.Add(new OpEmission(sb.ToString(), saveDepth, IsState: false));
				break;
			}

			var endPos = opStart + size;
			var name = OpNames.GetValueOrDefault(op, $"?_op{op}");

			switch (name)
			{
				case "SAVE":
					sb.AppendLine($"{indent}canvas.Save();");
					saveDepth++;
					break;
				case "RESTORE":
					sb.AppendLine($"{indent}canvas.Restore();");
					if (saveDepth > 0) saveDepth--;
					break;
				case "TRANSLATE":
					sb.AppendLine($"{indent}canvas.Translate({F(r.ReadSingle())}, {F(r.ReadSingle())});");
					break;
				case "SCALE":
					sb.AppendLine($"{indent}canvas.Scale({F(r.ReadSingle())}, {F(r.ReadSingle())});");
					break;
				case "ROTATE":
					sb.AppendLine($"{indent}canvas.RotateDegrees({F(r.ReadSingle())});");
					break;
				case "SKEW":
					sb.AppendLine($"{indent}canvas.Skew({F(r.ReadSingle())}, {F(r.ReadSingle())});");
					break;
				case "CONCAT44":
				case "SET_M44":
				{
					var m = new float[16];
					for (var i = 0; i < 16; i++) m[i] = r.ReadSingle();
					var call = name == "SET_M44" ? "SetMatrix" : "Concat";
					// Wire format is column-major (Skia's SkM44 fMat[c*4+r]).
					// SkiaSharp's SKMatrix44(m00…m33) constructor takes
					// row-major args, so transpose while emitting.
					sb.AppendLine($"{indent}// {name} (SkM44, column-major on the wire):");
					sb.AppendLine($"{indent}canvas.{call}(new SKMatrix44(");
					sb.AppendLine($"{indent}\t{F(m[0])}, {F(m[4])}, {F(m[8])}, {F(m[12])}, // row 0");
					sb.AppendLine($"{indent}\t{F(m[1])}, {F(m[5])}, {F(m[9])}, {F(m[13])}, // row 1");
					sb.AppendLine($"{indent}\t{F(m[2])}, {F(m[6])}, {F(m[10])}, {F(m[14])}, // row 2");
					sb.AppendLine($"{indent}\t{F(m[3])}, {F(m[7])}, {F(m[11])}, {F(m[15])}  // row 3");
					sb.AppendLine($"{indent}));");
					break;
				}
				case "CLIP_RECT":
				{
					var rect = ReadRect(r);
					var packed = r.ReadUInt32();
					_ = r.ReadUInt32(); // offsetToRestore
					sb.AppendLine($"{indent}canvas.ClipRect({rect}, {ClipOpFrom(packed)}, antialias: {ClipAaFrom(packed)});");
					break;
				}
				case "CLIP_RRECT":
				{
					var rect = ReadRect(r);
					var radii = new float[8];
					for (var i = 0; i < 8; i++) radii[i] = r.ReadSingle();
					var packed = r.ReadUInt32();
					_ = r.ReadUInt32();
					sb.AppendLine($"{indent}using (var rrect = new SKRoundRect()) {{ rrect.SetRectRadii({rect}, new[] {{ new SKPoint({F(radii[0])}, {F(radii[1])}), new SKPoint({F(radii[2])}, {F(radii[3])}), new SKPoint({F(radii[4])}, {F(radii[5])}), new SKPoint({F(radii[6])}, {F(radii[7])}) }}); canvas.ClipRoundRect(rrect, {ClipOpFrom(packed)}, antialias: {ClipAaFrom(packed)}); }}");
					break;
				}
				case "CLIP_PATH":
				{
					var pathI = r.ReadInt32();
					var packed = r.ReadUInt32();
					_ = r.ReadUInt32(); // offsetToRestore
					// Skia's playback of CLIP_PATH short-circuits the rest of a
					// save-block when the resulting clip is empty (isClipEmpty
					// skip-forward optimisation in SkPicturePlayback). We don't
					// emulate that skip, so applying the clip literally leaves
					// subsequent draws in the same save-block invisible in
					// Uno-style captures. Emit as a comment.
					if (pathI == 0)
						sb.AppendLine($"{indent}// CLIP_PATH idx=0 skipped");
					else
						sb.AppendLine($"{indent}// canvas.ClipPath(pathTable[{pathI}], {ClipOpFrom(packed)}, antialias: {ClipAaFrom(packed)}); // suppressed (see decoder note)");
					break;
				}
				case "DRAW_PAINT":
					sb.AppendLine($"{indent}canvas.DrawPaint(paintTable[{r.ReadInt32()}]);");
					break;
				case "DRAW_CLEAR":
					sb.AppendLine($"{indent}canvas.Clear({SKColorLiteral(r.ReadUInt32())});");
					break;
				case "DRAW_RECT":
				{
					var pi = r.ReadInt32();
					sb.AppendLine($"{indent}canvas.DrawRect({ReadRect(r)}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_OVAL":
				{
					var pi = r.ReadInt32();
					sb.AppendLine($"{indent}canvas.DrawOval({ReadRect(r)}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_RRECT":
				{
					var pi = r.ReadInt32();
					var rect = ReadRect(r);
					var radii = new float[8];
					for (var i = 0; i < 8; i++) radii[i] = r.ReadSingle();
					sb.AppendLine($"{indent}using (var rrect = new SKRoundRect()) {{ rrect.SetRectRadii({rect}, new[] {{ new SKPoint({F(radii[0])}, {F(radii[1])}), new SKPoint({F(radii[2])}, {F(radii[3])}), new SKPoint({F(radii[4])}, {F(radii[5])}), new SKPoint({F(radii[6])}, {F(radii[7])}) }}); canvas.DrawRoundRect(rrect, paintTable[{pi}]); }}");
					break;
				}
				case "DRAW_DRRECT":
				{
					var pi = r.ReadInt32();
					sb.AppendLine($"{indent}// DRAW_DRRECT paintIdx={pi}, 2× SkRRect (96 B) — not decoded");
					r.BaseStream.Position += 96;
					break;
				}
				case "DRAW_PATH":
				{
					var pi = r.ReadInt32();
					var pathI = r.ReadInt32();
					if (pathI == 0)
						sb.AppendLine($"{indent}// DRAW_PATH idx=0 (null path) skipped");
					else
						sb.AppendLine($"{indent}canvas.DrawPath(pathTable[{pathI}], paintTable[{pi}]);");
					break;
				}
				case "DRAW_POINTS":
				{
					var pi = r.ReadInt32();
					var mode = r.ReadInt32();
					var count = r.ReadInt32();
					sb.AppendLine($"{indent}var points_{opStart} = new SKPoint[{count}];");
					for (var i = 0; i < count; i++)
					{
						var x = r.ReadSingle();
						var y = r.ReadSingle();
						sb.AppendLine($"{indent}points_{opStart}[{i}] = new SKPoint({F(x)}, {F(y)});");
					}
					sb.AppendLine($"{indent}canvas.DrawPoints((SKPointMode){mode}, points_{opStart}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_ARC":
				{
					var pi = r.ReadInt32();
					var oval = ReadRect(r);
					var startAngle = r.ReadSingle();
					var sweep = r.ReadSingle();
					var useCenter = r.ReadInt32() != 0;
					sb.AppendLine($"{indent}canvas.DrawArc({oval}, {F(startAngle)}, {F(sweep)}, useCenter: {useCenter.ToString().ToLowerInvariant()}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_PICTURE":
				{
					var localIdx = r.ReadInt32();
					if (localIdx >= 1 && localIdx <= picture.SubPictures.Count)
					{
						var sub = picture.SubPictures[localIdx - 1];
						sb.AppendLine($"{indent}SubPic_{sub.GlobalId}.Draw(canvas);");
					}
					else
					{
						sb.AppendLine($"{indent}// DRAW_PICTURE picIdx={localIdx} — out of range (have {picture.SubPictures.Count})");
					}
					break;
				}
				case "DRAW_TEXT_BLOB":
				{
					var pi = r.ReadInt32();
					var bi = r.ReadInt32();
					var x = r.ReadSingle();
					var y = r.ReadSingle();
					if (bi == 0)
						sb.AppendLine($"{indent}// DRAW_TEXT_BLOB idx=0 (null blob) skipped");
					else
						sb.AppendLine($"{indent}if (textBlobTable[{bi}] is {{ }} b{opStart}) canvas.DrawText(b{opStart}, {F(x)}, {F(y)}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_IMAGE2":
				{
					var pi = r.ReadInt32();
					var ii = r.ReadInt32();
					var x = r.ReadSingle();
					var y = r.ReadSingle();
					r.ReadBytes(Math.Min(12, (int)(endPos - s.Position)));
					// idx 0 = null-sentinel in Skia's 1-based indexing; Skia's own
					// playback treats DrawImage(null) as a no-op but SkiaSharp
					// throws ArgumentNullException, so skip at emit.
					if (ii == 0)
						sb.AppendLine($"{indent}// DRAW_IMAGE2 idx=0 (null image) skipped");
					else
						sb.AppendLine($"{indent}if (imageTable[{ii}] is {{ }} img{opStart}) canvas.DrawImage(img{opStart}, {F(x)}, {F(y)}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_IMAGE_RECT2":
				{
					var pi = r.ReadInt32();
					var ii = r.ReadInt32();
					var src = ReadRect(r);
					var dst = ReadRect(r);
					r.ReadBytes(Math.Min(16, (int)(endPos - s.Position)));
					if (ii == 0)
						sb.AppendLine($"{indent}// DRAW_IMAGE_RECT2 idx=0 (null image) skipped");
					else
						sb.AppendLine($"{indent}if (imageTable[{ii}] is {{ }} img{opStart}) canvas.DrawImage(img{opStart}, {src}, {dst}, paintTable[{pi}]);");
					break;
				}
				case "SAVE_LAYER_SAVELAYERREC":
				{
					var flags = r.ReadInt32();
					// Read and discard the optional payload fields — they only appear
					// when the corresponding flag bit is set on the op.
					SKRect? bounds = null; int? paintIdx = null; int? backdropIdx = null; int? layerFlags = null;
					if ((flags & 0x01) != 0) bounds = ReadRectStruct(r);
					if ((flags & 0x02) != 0) paintIdx = r.ReadInt32();
					if ((flags & 0x04) != 0) backdropIdx = r.ReadInt32();
					if ((flags & 0x08) != 0) layerFlags = r.ReadInt32();

					sb.AppendLine($"{indent}{{");
					sb.AppendLine($"{indent}\tvar rec = new SKCanvasSaveLayerRec();");
					if (bounds is { } b) sb.AppendLine($"{indent}\trec.Bounds = new SKRect({F(b.Left)}, {F(b.Top)}, {F(b.Right)}, {F(b.Bottom)});");
					if (paintIdx is { } pi) sb.AppendLine($"{indent}\trec.Paint = paintTable[{pi}];");
					if (backdropIdx is { } bp) sb.AppendLine($"{indent}\t// Backdrop = paintTable[{bp}].ImageFilter — not resolved");
					if (layerFlags is { } lf) sb.AppendLine($"{indent}\trec.Flags = (SKCanvasSaveLayerRecFlags){lf};");
					sb.AppendLine($"{indent}\tcanvas.SaveLayer(in rec);");
					sb.AppendLine($"{indent}}}");
					saveDepth++;
					break;
				}
				case "FLUSH":
				case "NOOP":
					break;
				default:
				{
					var argLen = size - 4;
					var argBytes = argLen > 0 ? r.ReadBytes(Math.Min(argLen, 64)) : Array.Empty<byte>();
					sb.AppendLine($"{indent}// op {name} (code {op}), size {size} bytes — not decoded. First {argBytes.Length} args bytes: {Hex(argBytes)}");
					break;
				}
			}

			if (s.Position != endPos)
			{
				var remaining = endPos - s.Position;
				if (remaining > 0) r.BaseStream.Position += remaining;
				else if (remaining < 0)
					sb.AppendLine($"{indent}// overshot op boundary by {-remaining} bytes — decoder bug for op {name}. Resyncing.");
				s.Position = endPos;
			}

			ops.Add(new OpEmission(sb.ToString(), saveDepth, IsState: StateOnlyOps.Contains(name)));
		}
		return ops;
	}

	// Consumes the decoded ops and appends their text to a target builder.
	private static void AppendOps(StringBuilder into, IEnumerable<OpEmission> ops, string extraIndent = "")
	{
		if (extraIndent.Length == 0)
		{
			foreach (var op in ops)
				into.Append(op.Text);
			return;
		}

		// Re-indent every non-empty line of the op text by prepending
		// extraIndent to each. Op text carries baked-in "\t\t" (2-tab) indent
		// from DecodeOps; a nested emission needs deeper indentation.
		foreach (var op in ops)
		{
			var text = op.Text;
			var start = 0;
			for (var i = 0; i < text.Length; i++)
			{
				if (text[i] == '\n')
				{
					var lineEnd = i + 1;
					var line = text.Substring(start, lineEnd - start);
					// Skip re-indenting truly empty lines ("\n" alone).
					if (line.Length > 1 || !line.StartsWith("\n"))
						into.Append(extraIndent);
					into.Append(line);
					start = lineEnd;
				}
			}
			if (start < text.Length) // trailing chunk without newline
			{
				into.Append(extraIndent);
				into.Append(text, start, text.Length - start);
			}
		}
	}

	// Format helpers
	private static string F(float f)
	{
		if (float.IsNaN(f)) return "float.NaN";
		if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
		if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
		return f == (int)f
			? ((int)f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "f"
			: f.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "f";
	}

	private static SKRect ReadRectStruct(BinaryReader r)
		=> new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

	private static string ReadRect(BinaryReader r)
	{
		var rect = ReadRectStruct(r);
		return $"new SKRect({F(rect.Left)}, {F(rect.Top)}, {F(rect.Right)}, {F(rect.Bottom)})";
	}

	private static string SKColorLiteral(uint c)
	{
		var a = (byte)((c >> 24) & 0xFF);
		var rc = (byte)((c >> 16) & 0xFF);
		var g = (byte)((c >> 8) & 0xFF);
		var b = (byte)(c & 0xFF);
		return $"new SKColor(0x{rc:X2}, 0x{g:X2}, 0x{b:X2}, 0x{a:X2})";
	}

	// Skia ClipParams_pack packs the clip op in the low 4 bits and the
	// antialias flag in bit 4 (NOT bit 31). Kept together here so the two
	// helpers can never drift out of sync.
	private static string ClipOpFrom(uint packed)
	{
		var opBits = (int)(packed & 0x0F);
		return opBits switch
		{
			0 => "SKClipOperation.Difference",
			1 => "SKClipOperation.Intersect",
			_ => $"/* raw region-op {opBits} — approximated */ SKClipOperation.Intersect",
		};
	}

	private static string ClipAaFrom(uint packed) => ((packed >> 4) & 1) != 0 ? "true" : "false";

	private static string Hex(byte[] b)
	{
		var sb = new StringBuilder(b.Length * 3);
		foreach (var x in b) { sb.Append(x.ToString("x2")); sb.Append(' '); }
		return sb.ToString().TrimEnd();
	}
}
