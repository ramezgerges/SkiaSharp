using System.IO;

namespace SkiaSharp.Tests;

public partial class DefaultTestConfig : TestConfig
{
	public DefaultTestConfig()
	{
		PathRoot = ResolvePathRoot();
	}

	// Two modes:
	//   1. Standalone zip — Content/ is copied next to the exe; PathRoot is
	//      just AppContext.BaseDirectory.
	//   2. Source-tree run — BDN launches the harness from a nested artifacts
	//      subdir where Content wasn't copied. Walk up until we see build.cake,
	//      then point at tests/Content.
	static string ResolvePathRoot()
	{
		var baseDir = Path.GetDirectoryName(typeof(DefaultTestConfig).Assembly.Location)!;
		if (Directory.Exists(Path.Combine(baseDir, "Content")))
			return baseDir;

		var dir = new DirectoryInfo(baseDir);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "build.cake")))
			dir = dir.Parent;
		if (dir is null)
			throw new DirectoryNotFoundException(
				"Cannot locate Content/ — expected either alongside the exe or under an ancestor with build.cake.");
		return Path.Combine(dir.FullName, "tests");
	}
}
