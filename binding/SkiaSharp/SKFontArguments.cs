using System;

namespace SkiaSharp;

public ref struct SKFontArguments
{
	public ReadOnlySpan<SKFontVariationPositionCoordinate> VariationDesignPosition { get; set; }

	public int CollectionIndex { get; set; }
}
