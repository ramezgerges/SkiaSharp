using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// Discovers every <see cref="IRenderBackend"/> in the assembly via the same
/// reflection helper the scene catalog uses. Add a backend by dropping a public
/// non-abstract <see cref="IRenderBackend"/> with a parameterless constructor
/// under <c>Backends/</c>; it appears in every benchmark's params column
/// automatically.
/// </summary>
internal static class BackendCatalog
{
	private static readonly Lazy<IReadOnlyDictionary<string, Type>> byName =
		new(Discover);

	public static IEnumerable<string> AllNames => byName.Value.Keys;

	public static IRenderBackend Create(string name)
	{
		if (!byName.Value.TryGetValue(name, out var type))
			throw new ArgumentException(
				$"Unknown backend '{name}'. Known: {string.Join(", ", byName.Value.Keys)}");
		return (IRenderBackend)Activator.CreateInstance(type)!;
	}

	private static IReadOnlyDictionary<string, Type> Discover()
	{
		var dict = new SortedDictionary<string, Type>(StringComparer.Ordinal);
		foreach (var type in CatalogReflection.ConcreteImplementations<IRenderBackend>())
		{
			var probe = (IRenderBackend)Activator.CreateInstance(type)!;
			dict[probe.Name] = type;
			probe.Dispose();
		}
		return dict;
	}
}
