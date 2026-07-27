using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering.Scenes;

/// <summary>
/// Discovers every <c>.skp</c> file under <c>Captures/</c> (deployed next to the
/// exe by the csproj) and turns each into a <see cref="PicturePlaybackScene"/>.
/// Merged with the reflection-based <see cref="SceneCatalog"/> at benchmark
/// registration time; the merged list is what BenchmarkDotNet parameterizes
/// <c>Scene</c> over.
/// </summary>
public static class CapturedSceneRegistry
{
	private static readonly Lazy<IReadOnlyDictionary<string, PicturePlaybackScene>> byName =
		new(Discover);

	public static IEnumerable<string> AllNames => byName.Value.Keys;

	public static bool TryGet(string name, out PicturePlaybackScene scene)
	{
		var found = byName.Value.TryGetValue(name, out var s);
		scene = s!;
		return found;
	}

	private static IReadOnlyDictionary<string, PicturePlaybackScene> Discover()
	{
		var dict = new SortedDictionary<string, PicturePlaybackScene>(StringComparer.Ordinal);
		foreach (var path in CandidatePaths())
		{
			try
			{
				var scene = PicturePlaybackScene.FromFile(path);
				dict[scene.Name] = scene;
			}
			catch (Exception ex)
			{
				// Bad .skp shouldn't take down the whole matrix — surface it and skip.
				Console.Error.WriteLine($"[CapturedSceneRegistry] skipping '{path}': {ex.Message}");
			}
		}
		return dict;
	}

	private static IEnumerable<string> CandidatePaths()
	{
		// Deployed layout: <exe dir>/Captures/*.skp (see csproj Content Include).
		var deployed = Path.Combine(AppContext.BaseDirectory, "Captures");
		if (Directory.Exists(deployed))
		{
			foreach (var f in Directory.EnumerateFiles(deployed, "*.skp", SearchOption.AllDirectories))
				yield return f;
		}
	}
}
