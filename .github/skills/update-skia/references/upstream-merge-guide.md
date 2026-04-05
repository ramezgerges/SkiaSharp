# Upstream Merge Guide for mono/skia

How to properly merge a new Google Skia milestone into the mono/skia fork while preserving SkiaSharp's C API layer and full commit attribution.

## Background

The mono/skia fork adds a C API wrapper layer on top of upstream Google Skia:
- `include/c/*.h` (~37 headers)
- `src/c/*.cpp` (~30 implementations)
- `src/xamarin/*.cpp` (~10 utility files)
- `BUILD.gn` additions (`skiasharp_build("SkiaSharp")` target)

These ~97 files do NOT exist in upstream Google Skia. When merging a new milestone, these must be carried forward with proper git history and blame attribution.

## The Golden Rule: Genuine Conflict-Resolved Merge

```bash
git merge --no-commit upstream/chrome/m{TARGET}
# Resolve each conflict individually (see per-file guide below)
git commit
```

### Why Not Tree-Override?

A tree-override merge (`git merge -s ours`, or `git read-tree --reset` after `--no-commit`) records parentage without actually combining content. This causes:

1. **`git blame` is destroyed** — every line in every file is attributed to the merge commit, not original authors
2. **`git log --follow` breaks** — can't trace file history through the merge
3. **Cherry-picks needed afterward** — C API files must be re-added as separate commits, further obscuring history
4. **Misleading history** — merge claims to include both parents but actually discarded one side entirely

## Merge Scale (What to Expect)

The number of conflicts scales with how many milestones are being jumped. Expect conflicts in `BUILD.gn`, `DEPS`, infrastructure files, and any upstream headers that SkiaSharp patches. C API files (`include/c/`, `src/c/`) rarely conflict since they don't exist upstream — but verify they all survive the merge.

## Conflict Resolution by File Category

### BUILD.gn — **Combine Both** (Most Complex)

This is always the hardest file. Both sides modify it heavily.

**Keep from upstream:**
- Modern build structure, updated source file lists, new modules
- Removed obsolete flags and targets

**Keep from SkiaSharp:**
- `skiasharp_build("SkiaSharp")` target (usually near line ~3500+)
- Platform flags: `is_winrt`, `is_watchos`, `is_tvos`, `is_maccatalyst`
- `SKIA_C_DLL` define in `extra_cflags`
- All `src/c/*.cpp` and `src/xamarin/*.cpp` source listings
- All `include/c/*.h` header listings

**Watch out for:**
- Legacy defines like `SK_DEFAULT_TYPEFACE_IS_EMPTY` and `SK_DISABLE_LEGACY_DEFAULT_TYPEFACE` — these may break SkiaSharp's C API. Comment them out if they cause compilation errors.

### DEPS — **Keep our DEPS pins, accept upstream structure**

SkiaSharp customizes Skia's dependency pins. Accept upstream structure, but keep SkiaSharp's DEPS pins. If SkiaSharp comments out specific entries (like emsdk), preserve those comments.

### C API Headers (`include/c/`) — **Keep SkiaSharp**

These files don't exist upstream. Conflicts are rare. If they occur, always keep SkiaSharp's version — the files are entirely SkiaSharp additions.

### C API Source (`src/c/`) — **Keep SkiaSharp + Adapt Later**

Take SkiaSharp's implementations during merge resolution. Then apply focused post-merge commits to adapt them:
- Update `#include` paths for header reorganizations
- Update C++ API calls to use current function names/namespaces
- Update struct references for type renames

### Infrastructure Files

| File Type | Strategy |
|-----------|----------|
| `.gitignore` | Combine both |
| CI job definitions (`infra/bots/`) | Take upstream |
| `.disabled.*` configs | Keep SkiaSharp |
| `RELEASE_NOTES.md` | Take upstream |
| Fontconfig/Tizen support | Combine |

## Post-Merge Commits

After the merge commit, apply targeted adaptation commits. Keep these **separate** from the merge for clear blame:

### Commit 1: C API Adaptations

Update C API files that need changes for the new milestone:
- Include path updates, API call updates, struct changes

### Commit 2: Build Adjustments (if needed)

Any remaining BUILD.gn tweaks or build configuration changes.

## Verification Checklist

After merge and post-merge commits:

```bash
# 1. Verify both parents visible in history
git log --oneline --graph HEAD~5..HEAD

# 2. Verify skiasharp commits reachable
git log --all --oneline skiasharp..HEAD | head -5

# 3. Verify blame attribution preserved
git blame src/c/sk_canvas.cpp | head -20
# Should show original commit SHAs, not just the merge commit

# 4. Count C API files (expect ~97)
find include/c src/c src/xamarin \( -name "*.h" -o -name "*.cpp" \) | wc -l

# 5. Verify no auto-merge errors in key files
git diff HEAD -- BUILD.gn | head -50  # Sanity check

# 6. Verify SkiaSharp build target present
grep -n "skiasharp_build" BUILD.gn
```

## Common Pitfalls

1. **Auto-merged files may still be wrong**: Git's auto-merge can produce incorrect results when both sides modified the same logical section in non-overlapping lines. Review auto-merged files for correctness.

2. **New SkiaSharp-only files**: ~103 files that only exist in SkiaSharp are auto-added during merge. Verify they're all present — Git may silently drop some during complex merges.

3. **Deleted upstream files**: If upstream deleted files that SkiaSharp references, the merge may silently succeed but the build will fail. Check for missing includes.

4. **Header/implementation mismatch**: After merge, verify that header declarations in `include/c/` still match implementations in `src/c/`. Custom method declarations can be silently removed by upstream changes to shared headers.

## Typical Conflict Patterns

These are the most common conflict types across milestone bumps:

| File | Conflict Type | Resolution Pattern |
|------|--------------|-------------------|
| `BUILD.gn` | Both sides modified heavily | Combine: upstream structure + SkiaSharp targets/flags |
| `infra/bots/jobs.json` | Both sides modified | Take upstream |
| Platform support configs | SkiaSharp-specific platform additions | Combine: upstream base + SkiaSharp's additions |
