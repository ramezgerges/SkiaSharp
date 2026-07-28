using System;
using System.Collections.Generic;
using System.IO;
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
		string[] EffectSummaries); // one per: pathEffect, shader, maskFilter, colorFilter, imageFilter, blender

	private sealed class ParsedSkp
	{
		public uint Version;
		public float CullLeft, CullTop, CullRight, CullBottom;
		public byte[] OpData = Array.Empty<byte>();
		public List<string> FactoryNames = new();
		public List<ParsedPaint> Paints = new();
		public int PathCount, TextBlobCount, VerticesCount, ImageCount;
		public int SubPictureCount;
		public long ArrayBufferBytes;
	}

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

		// Outer tag stream.
		while (stream.Position < stream.Length)
		{
			var tag = ReadTag(reader);
			if (tag == "eof ") break;
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
					// size = COUNT of typefaces (not bytes). Each typeface is
					// variable-size with no length prefix, so skipping past a
					// non-empty typeface table requires full SkTypeface parsing —
					// out of scope. Handle count == 0 (no text in the picture)
					// by falling through to the next tag; anything else, bail.
					if (size > 0)
					{
						stream.Position = stream.Length; // stop the loop
						break;
					}
					continue;
				case "aray":
					p.ArrayBufferBytes = size;
					ParseArrayBuffer(reader.ReadBytes((int)size), p);
					break;
				case "pctr":
					p.SubPictureCount = (int)size;
					// Sub-pictures follow but recursing is out-of-scope here.
					return p;
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
					p.PathCount = (int)count;
					// SkPath format: writeInt(count) again, then paths. Path parsing
					// isn't done here — skip to next tag or end.
					return;
				case "blob":
					p.TextBlobCount = (int)count;
					return;
				case "vert":
					p.VerticesCount = (int)count;
					return;
				case "imag":
					p.ImageCount = (int)count;
					return;
				case "slug":
					return; // count of slugs; skip
				default:
					return; // unknown — stop cleanly
			}
		}
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

		var effects = new string[6];
		if (hasEffects)
		{
			var labels = new[] { "PathEffect", "Shader", "MaskFilter", "ColorFilter", "ImageFilter", "Blender" };
			for (var i = 0; i < 6; i++)
				effects[i] = ReadFlattenableSummary(r, factoryNames, labels[i]);
		}

		return new ParsedPaint(
			strokeWidth, strokeMiter,
			rC, gC, bC, aC,
			antiAlias, dither,
			blend, strokeCap, strokeJoin, style,
			hasEffects, effects);
	}

	private static string ReadFlattenableSummary(BinaryReader r, List<string> factoryNames, string label)
	{
		var idx = r.ReadUInt32();
		if (idx == 0)
			return $"{label}=null";
		// The SkPicture path uses the factory-set encoding: `idx` is a 1-based
		// index into the "fact" tag's name table. The alternate string-name
		// encoding is signalled by having the low byte be 0 and the rest be a
		// dict index (idx << 8); we don't see that in SkPicture serialization,
		// but tolerate the sentinel.
		string name;
		if ((idx & 0xFF) == 0)
			name = $"<dict-idx {idx >> 8}>";
		else
			name = idx >= 1 && idx <= factoryNames.Count
				? factoryNames[(int)idx - 1]
				: $"<factory {idx}>";

		var payloadSize = r.ReadUInt32();
		var startOffset = r.BaseStream.Position;
		// Skip the payload — parsing the flattenable's internal format is
		// per-subclass work.
		r.BaseStream.Position += payloadSize;
		return $"{label}={name} (payload {payloadSize} B at buffer offset {startOffset})";
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
		sb.AppendLine("\t// Path / text-blob / image tables — not decoded. Playback of ops that");
		sb.AppendLine("\t// reference these will throw at runtime unless populated by hand.");
		sb.AppendLine("\tprivate static readonly SKPath[] pathTable = new SKPath[0];");
		sb.AppendLine("\tprivate static readonly SKImage[] imageTable = new SKImage[0];");
		sb.AppendLine("\tprivate static readonly SKTextBlob[] textBlobTable = new SKTextBlob[0];");
		sb.AppendLine();

		sb.AppendLine("\tpublic void Draw(SKCanvas canvas)");
		sb.AppendLine("\t{");
		AppendOps(sb, DecodeOps(p.OpData));
		sb.AppendLine("\t}");
		sb.AppendLine("}");
		sb.AppendLine();
		sb.AppendLine($"// -- header --");
		sb.AppendLine($"// version = {p.Version}");
		sb.AppendLine($"// op stream = {p.OpData.Length} bytes; array buffer = {p.ArrayBufferBytes} bytes");
		return sb.ToString();
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
		var ops = DecodeOps(p.OpData);
		var (contentPrologue, statePrologue, body, stateEpilogue, baselineDepth) = SegmentOps(ops);
		var buckets = PartitionBody(body, partitionCount, baselineDepth);
		var actual = buckets.Count;

		var sb = new StringBuilder();

		sb.AppendLine("// Auto-generated by SkpDecompiler --partition. Reproduces the drawing");
		sb.AppendLine("// captured in an .skp file, split into N methods that can be recorded");
		sb.AppendLine("// in parallel on separate Graphite Recorders.");
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
		sb.AppendLine("\tprivate static readonly SKPath[] pathTable = new SKPath[0];");
		sb.AppendLine("\tprivate static readonly SKImage[] imageTable = new SKImage[0];");
		sb.AppendLine("\tprivate static readonly SKTextBlob[] textBlobTable = new SKTextBlob[0];");
		sb.AppendLine();

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

		sb.AppendLine("}");
		sb.AppendLine();
		sb.AppendLine($"// -- header --");
		sb.AppendLine($"// version = {p.Version}");
		sb.AppendLine($"// op stream = {p.OpData.Length} bytes; array buffer = {p.ArrayBufferBytes} bytes");
		sb.AppendLine($"// requested partitions = {partitionCount}; actual = {actual}");
		for (var i = 0; i < actual; i++)
			sb.AppendLine($"// partition {i}: {buckets[i].Count} ops");
		return sb.ToString();
	}

	private static void EmitPaint(StringBuilder sb, int idx, ParsedPaint pt)
	{
		sb.Append($"\t\tpaints[{idx}] = new SKPaint {{ ");
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
		if (pt.HasEffects)
		{
			foreach (var eff in pt.EffectSummaries)
				if (!eff.EndsWith("=null"))
					sb.AppendLine($"\t\t// paints[{idx}].{eff}");
		}
	}

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

	private static List<OpEmission> DecodeOps(byte[] opData)
	{
		var ops = new List<OpEmission>();
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
					sb.AppendLine($"{indent}// {name} (SkM44, column-major):");
					sb.AppendLine($"{indent}canvas.{call}(new SKMatrix44 {{");
					for (var col = 0; col < 4; col++)
					{
						var vals = string.Join(", ", new[] { m[col * 4], m[col * 4 + 1], m[col * 4 + 2], m[col * 4 + 3] });
						sb.AppendLine($"{indent}\t/* col {col} */ {vals},");
					}
					sb.AppendLine($"{indent}}});");
					break;
				}
				case "CLIP_RECT":
				{
					var rect = ReadRect(r);
					var packed = r.ReadUInt32();
					_ = r.ReadUInt32(); // offsetToRestore
					sb.AppendLine($"{indent}canvas.ClipRect({rect}, {ClipOpFrom(packed)}, antialias: {(((packed & 0x8000_0000) != 0) ? "true" : "false")});");
					break;
				}
				case "CLIP_RRECT":
				{
					var rect = ReadRect(r);
					var radii = new float[8];
					for (var i = 0; i < 8; i++) radii[i] = r.ReadSingle();
					var packed = r.ReadUInt32();
					_ = r.ReadUInt32();
					sb.AppendLine($"{indent}using (var rrect = new SKRoundRect()) {{ rrect.SetRectRadii({rect}, new[] {{ new SKPoint({F(radii[0])}, {F(radii[1])}), new SKPoint({F(radii[2])}, {F(radii[3])}), new SKPoint({F(radii[4])}, {F(radii[5])}), new SKPoint({F(radii[6])}, {F(radii[7])}) }}); canvas.ClipRoundRect(rrect, {ClipOpFrom(packed)}); }}");
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
					sb.AppendLine($"{indent}// DRAW_PICTURE picIdx={r.ReadInt32()} — nested SKPicture; not resolved");
					break;
				case "DRAW_TEXT_BLOB":
				{
					var pi = r.ReadInt32();
					var bi = r.ReadInt32();
					var x = r.ReadSingle();
					var y = r.ReadSingle();
					sb.AppendLine($"{indent}canvas.DrawText(textBlobTable[{bi}], {F(x)}, {F(y)}, paintTable[{pi}]);");
					break;
				}
				case "DRAW_IMAGE2":
				{
					var pi = r.ReadInt32();
					var ii = r.ReadInt32();
					var x = r.ReadSingle();
					var y = r.ReadSingle();
					r.ReadBytes(Math.Min(12, (int)(endPos - s.Position)));
					sb.AppendLine($"{indent}canvas.DrawImage(imageTable[{ii}], {F(x)}, {F(y)}, /* sampling default */ paintTable[{pi}]);");
					break;
				}
				case "DRAW_IMAGE_RECT2":
				{
					var pi = r.ReadInt32();
					var ii = r.ReadInt32();
					var src = ReadRect(r);
					var dst = ReadRect(r);
					r.ReadBytes(Math.Min(16, (int)(endPos - s.Position)));
					sb.AppendLine($"{indent}canvas.DrawImage(imageTable[{ii}], {src}, {dst}, /* sampling */ paintTable[{pi}]);");
					break;
				}
				case "SAVE_LAYER_SAVELAYERREC":
				{
					var flags = r.ReadInt32();
					sb.Append($"{indent}canvas.SaveLayer(in new SKCanvasSaveLayerRec {{");
					if ((flags & 0x01) != 0) sb.Append($" Bounds = {ReadRect(r)},");
					if ((flags & 0x02) != 0) sb.Append($" Paint = paintTable[{r.ReadInt32()}],");
					if ((flags & 0x04) != 0) sb.Append($" Backdrop = /* paintTable[{r.ReadInt32()}].ImageFilter */ null,");
					if ((flags & 0x08) != 0) sb.Append($" Flags = (SKCanvasSaveLayerRecFlags){r.ReadInt32()},");
					sb.AppendLine(" });");
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
	private static void AppendOps(StringBuilder into, IEnumerable<OpEmission> ops)
	{
		foreach (var op in ops)
			into.Append(op.Text);
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

	private static string Hex(byte[] b)
	{
		var sb = new StringBuilder(b.Length * 3);
		foreach (var x in b) { sb.Append(x.ToString("x2")); sb.Append(' '); }
		return sb.ToString().TrimEnd();
	}
}
