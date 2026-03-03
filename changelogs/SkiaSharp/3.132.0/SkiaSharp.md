# API diff: SkiaSharp.dll

## SkiaSharp.dll

> Assembly Version Changed: 3.132.0.0 vs 3.119.0.0

### Breaking Changes (Upstream Skia m132)

#### Removed APIs

The following C API functions were removed because the underlying Skia APIs no longer exist in m132:

- `sk_font_break_text` — `SkFont::breakText()` was removed. The C API stub now returns 0.
- `sk_text_utils_get_pos_path` — `SkTextUtils::GetPosPath()` was removed. The C API stub is now a no-op.
- `sk_refcnt_get_ref_count` / `sk_nvrefcnt_get_ref_count` — `SkRefCnt::getRefCount()` was removed from the public API. Both now return -1.

#### Deprecated APIs (Still Compiles, Future Removal)

- `sk_maskfilter_new_table`, `sk_maskfilter_new_gamma`, `sk_maskfilter_new_clip` — `SkTableMaskFilter` is deprecated in Skia and will be removed in a future release.
- `sk_maskfilter_new_shader` — `SkShaderMaskFilter` is deprecated in Skia and will be removed in a future release.

### Namespace SkiaSharp

#### Type Changed: SkiaSharp.SKColorType

Added values:

```csharp
Bgra10101010Xr = 25,
RgbF16F16F16x = 26,
```

#### Type Changed: SkiaSharp.SKJpegEncoderOptions

Added fields for EXIF orientation support (new in Skia m132):

```csharp
// int32_t fOrigin — SkEncodedOrigin value (0 = not set)
// bool fHasOrigin — whether fOrigin is set
```

### Native API Changes (C Layer)

#### Header Changes

- `GrMipMapped` renamed to `skgpu::Mipmapped`
- `GrDirectContext::MakeGL/MakeVulkan/MakeMetal` moved to `GrDirectContexts::MakeGL/MakeVulkan/MakeMetal`
- `GrBackendTexture::hasMipMaps()` renamed to `hasMipmaps()`
- `SkTextBlobBuilder::allocRunRSXform` signature changed (bounds parameter removed)
- `skresources::FileResourceProvider::Make` and `DataURIResourceProviderProxy::Make` now take `ImageDecodeStrategy` enum instead of `bool predecode`
- `SkXPS::MakeDocument` is now only available on Windows (`SK_BUILD_FOR_WIN`)

#### New SkColorType Values

Two new color types added to the `sk_colortype_t` enum:
- `BGRA_10101010_XR_SK_COLORTYPE` (index 12)
- `RGB_F16F16F16X_SK_COLORTYPE` (index 17)

This shifts all subsequent native enum values. The managed `SKColorTypeNative` enum has been updated to match.

### Build System Changes

- Removed `skia_use_sfntly=false` GN flag from all 14 platform build scripts (flag removed in m132)
- Added `linux_soname_version` GN arg declaration for Linux shared library versioning
- Added Vulkan include guards (`#if SK_VULKAN`) to `gr_context.cpp`
- Added GPU/Vulkan/D3D conditional compilation guards to `sk_types_priv.h`
