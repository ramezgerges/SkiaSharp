# Files Changed in a Typical Update

| Repository | File | Change |
|-----------|------|--------|
| mono/skia | `DEPS` | Merge conflict resolution |
| mono/skia | `BUILD.gn` | Merge conflict resolution (most complex) |
| mono/skia | `include/core/SkMilestone.h` | New milestone number (from upstream) |
| mono/skia | `include/c/sk_types.h` | Enum/type updates |
| mono/skia | `src/c/*.cpp` | C API fixes for new C++ APIs |
| mono/skia | `src/c/sk_enums.cpp` | Enum mapping updates |
| mono/skia | `src/c/sk_types_priv.h` | Include path + type conversion updates |
| mono/SkiaSharp | `.gitmodules` | Submodule branch name |
| mono/SkiaSharp | `externals/skia` | Submodule pointer |
| mono/SkiaSharp | `scripts/VERSIONS.txt` | All version numbers |
| mono/SkiaSharp | `cgmanifest.json` | Security tracking |
| mono/SkiaSharp | `scripts/azure-pipelines-variables.yml` | CI config |
| mono/SkiaSharp | `scripts/cake/native-shared.cake` | Build script adjustments (e.g., emsdk skip) |
| mono/SkiaSharp | `native/*/build.cake` | Per-platform GN flag updates |
| mono/SkiaSharp | `binding/SkiaSharp/SkiaApi.generated.cs` | Regenerated |
| mono/SkiaSharp | `binding/SkiaSharp/Definitions.cs` | Type definitions, new enums |
| mono/SkiaSharp | `binding/SkiaSharp/EnumMappings.cs` | Enum mappings |
| mono/SkiaSharp | `binding/SkiaSharp/GRDefinitions.cs` | GPU type changes |
| mono/SkiaSharp | `binding/libSkiaSharp.json` | Type config |
| mono/SkiaSharp | `changelogs/SkiaSharp/3.{TARGET}.0/SkiaSharp.md` | Changelog (new file) |
| mono/SkiaSharp | `tests/Tests/SkiaSharp/*.cs` | Test updates |
