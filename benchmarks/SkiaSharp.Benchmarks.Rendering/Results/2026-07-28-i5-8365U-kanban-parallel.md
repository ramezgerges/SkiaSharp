```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8875/25H2/2025Update/HudsonValley2)
Intel Core i5-8365U CPU 1.60GHz (Max: 1.90GHz) (Coffee Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.300
  [Host] : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

Arguments=/p:SkipMDocGenerateDocs=true  Toolchain=InProcessEmitToolchain  

```
| Method      | Backend         | Scene                | Mean          | Error      | StdDev     | Allocated |
|------------ |---------------- |--------------------- |--------------:|-----------:|-----------:|----------:|
| **RenderScene** | **ganesh-vulkan**   | **AdvancedBlend**        |     **754.32 μs** |  **12.069 μs** |  **11.290 μs** |     **360 B** |
| **RenderScene** | **ganesh-vulkan**   | **BackdropBlur**         |   **1,069.25 μs** |  **21.373 μs** |  **51.618 μs** |     **290 B** |
| **RenderScene** | **ganesh-vulkan**   | **Captu(...)Blend [29]** |     **654.15 μs** |  **18.434 μs** |  **53.772 μs** |       **5 B** |
| **RenderScene** | **ganesh-vulkan**   | **ColorFilterFade**      |     **666.23 μs** |  **19.071 μs** |  **55.933 μs** |     **301 B** |
| **RenderScene** | **ganesh-vulkan**   | **ColorFilterMatrix**    |     **718.25 μs** |  **26.825 μs** |  **79.094 μs** |     **302 B** |
| **RenderScene** | **ganesh-vulkan**   | **CubicBezier**          |  **14,584.66 μs** | **286.517 μs** | **454.446 μs** |   **33767 B** |
| **RenderScene** | **ganesh-vulkan**   | **DiagonalLines**        |     **722.44 μs** |  **17.577 μs** |  **51.551 μs** |      **93 B** |
| **RenderScene** | **ganesh-vulkan**   | **DrawArcs**             |     **969.33 μs** |  **21.891 μs** |  **64.201 μs** |     **181 B** |
| **RenderScene** | **ganesh-vulkan**   | **DrawAtlas**            |     **643.53 μs** |  **23.007 μs** |  **67.837 μs** |    **6453 B** |
| **RenderScene** | **ganesh-vulkan**   | **DrawPoints**           |   **1,658.82 μs** |  **32.515 μs** |  **43.407 μs** |    **2522 B** |
| **RenderScene** | **ganesh-vulkan**   | **DrawVertices**         |     **664.94 μs** |  **20.508 μs** |  **60.146 μs** |    **6790 B** |
| **RenderScene** | **ganesh-vulkan**   | **FilledCircle**         |     **600.48 μs** |  **24.799 μs** |  **73.121 μs** |      **93 B** |
| **RenderScene** | **ganesh-vulkan**   | **GradientBlend**        |     **667.61 μs** |  **26.458 μs** |  **76.760 μs** |     **365 B** |
| **RenderScene** | **ganesh-vulkan**   | **ImageFilterChain**     |   **2,514.70 μs** |  **48.326 μs** |  **62.837 μs** |     **717 B** |
| **RenderScene** | **ganesh-vulkan**   | **LargeImage**           |     **673.76 μs** |  **13.424 μs** |  **29.746 μs** |       **6 B** |
| **RenderScene** | **ganesh-vulkan**   | **OpacityLayers**        |   **1,178.24 μs** |  **23.544 μs** |  **43.052 μs** |     **802 B** |
| **RenderScene** | **ganesh-vulkan**   | **PictureCache**         |   **1,011.25 μs** |  **19.939 μs** |  **43.766 μs** |       **5 B** |
| **RenderScene** | **ganesh-vulkan**   | **RadialSweepGradient**  |     **715.59 μs** |  **23.761 μs** |  **69.686 μs** |    **1277 B** |
| **RenderScene** | **ganesh-vulkan**   | **RedRo(...)White [21]** |     **572.27 μs** |  **27.946 μs** |  **82.400 μs** |      **93 B** |
| **RenderScene** | **ganesh-vulkan**   | **RRectBlur**            |     **826.47 μs** |  **20.048 μs** |  **59.111 μs** |     **286 B** |
| **RenderScene** | **ganesh-vulkan**   | **ShaderMask**           |     **877.09 μs** |  **20.054 μs** |  **58.814 μs** |     **365 B** |
| **RenderScene** | **ganesh-vulkan**   | **SuperellipseBlur**     |     **971.77 μs** |  **25.994 μs** |  **74.581 μs** |     **291 B** |
| **RenderScene** | **ganesh-vulkan**   | **Text**                 |   **1,425.91 μs** |  **28.211 μs** |  **72.315 μs** |    **1080 B** |
| **RenderScene** | **graphite-vulkan** | **AdvancedBlend**        |     **978.65 μs** |  **19.569 μs** |  **48.733 μs** |     **475 B** |
| **RenderScene** | **graphite-vulkan** | **BackdropBlur**         |   **1,314.36 μs** |  **26.266 μs** |  **59.821 μs** |     **394 B** |
| **RenderScene** | **graphite-vulkan** | **Captu(...)Blend [29]** |     **757.32 μs** |  **20.751 μs** |  **60.860 μs** |     **109 B** |
| **RenderScene** | **graphite-vulkan** | **ColorFilterFade**      |     **817.30 μs** |  **32.315 μs** |  **95.281 μs** |     **405 B** |
| **RenderScene** | **graphite-vulkan** | **ColorFilterMatrix**    |     **768.78 μs** |  **15.225 μs** |  **41.678 μs** |     **406 B** |
| **RenderScene** | **graphite-vulkan** | **CubicBezier**          |  **14,678.89 μs** | **149.240 μs** | **132.297 μs** |   **33880 B** |
| **RenderScene** | **graphite-vulkan** | **DiagonalLines**        |     **694.97 μs** |  **17.138 μs** |  **49.719 μs** |     **197 B** |
| **RenderScene** | **graphite-vulkan** | **DrawArcs**             |   **1,211.72 μs** |  **23.921 μs** |  **54.480 μs** |     **290 B** |
| **RenderScene** | **graphite-vulkan** | **DrawAtlas**            |     **547.74 μs** |  **30.463 μs** |  **89.344 μs** |    **6558 B** |
| **RenderScene** | **graphite-vulkan** | **DrawPoints**           |   **2,415.30 μs** |  **47.920 μs** |  **78.734 μs** |    **2637 B** |
| **RenderScene** | **graphite-vulkan** | **DrawVertices**         |     **707.90 μs** |  **19.933 μs** |  **58.459 μs** |    **6894 B** |
| **RenderScene** | **graphite-vulkan** | **FilledCircle**         |     **671.32 μs** |  **20.279 μs** |  **59.794 μs** |     **198 B** |
| **RenderScene** | **graphite-vulkan** | **GradientBlend**        |     **743.05 μs** |  **17.068 μs** |  **49.790 μs** |     **469 B** |
| **RenderScene** | **graphite-vulkan** | **ImageFilterChain**     |   **2,907.19 μs** |  **44.656 μs** |  **41.771 μs** |     **821 B** |
| **RenderScene** | **graphite-vulkan** | **LargeImage**           | **103,526.79 μs** | **194.645 μs** | **182.071 μs** |    **3259 B** |
| **RenderScene** | **graphite-vulkan** | **OpacityLayers**        |   **1,518.90 μs** |  **29.702 μs** |  **54.313 μs** |     **907 B** |
| **RenderScene** | **graphite-vulkan** | **PictureCache**         |   **1,132.98 μs** |  **22.611 μs** |  **64.143 μs** |     **115 B** |
| **RenderScene** | **graphite-vulkan** | **RadialSweepGradient**  |     **749.08 μs** |  **15.959 μs** |  **46.300 μs** |    **1381 B** |
| **RenderScene** | **graphite-vulkan** | **RedRo(...)White [21]** |     **610.17 μs** |  **28.600 μs** |  **84.327 μs** |     **197 B** |
| **RenderScene** | **graphite-vulkan** | **RRectBlur**            |   **3,105.16 μs** |  **46.408 μs** |  **43.410 μs** |     **405 B** |
| **RenderScene** | **graphite-vulkan** | **ShaderMask**           |     **982.92 μs** |  **19.503 μs** |  **47.841 μs** |     **469 B** |
| **RenderScene** | **graphite-vulkan** | **SuperellipseBlur**     |   **2,946.20 μs** |  **58.063 μs** |  **81.396 μs** |     **405 B** |
| **RenderScene** | **graphite-vulkan** | **Text**                 |   **1,474.76 μs** |  **29.213 μs** |  **68.284 μs** |    **1194 B** |
| **RenderScene** | **raster**          | **AdvancedBlend**        |  **10,151.59 μs** |  **75.988 μs** |  **71.079 μs** |     **444 B** |
| **RenderScene** | **raster**          | **BackdropBlur**         |  **36,270.66 μs** | **359.615 μs** | **336.384 μs** |     **664 B** |
| **RenderScene** | **raster**          | **Captu(...)Blend [29]** |   **4,491.57 μs** |  **43.683 μs** |  **40.861 μs** |      **42 B** |
| **RenderScene** | **raster**          | **ColorFilterFade**      |     **774.24 μs** |   **4.601 μs** |   **3.842 μs** |     **302 B** |
| **RenderScene** | **raster**          | **ColorFilterMatrix**    |     **144.98 μs** |   **2.813 μs** |   **4.380 μs** |     **297 B** |
| **RenderScene** | **raster**          | **CubicBezier**          |  **17,318.28 μs** | **171.215 μs** | **142.973 μs** |   **33865 B** |
| **RenderScene** | **raster**          | **DiagonalLines**        |     **490.64 μs** |   **6.581 μs** |   **6.156 μs** |      **93 B** |
| **RenderScene** | **raster**          | **DrawArcs**             |   **3,273.91 μs** |  **17.935 μs** |  **16.776 μs** |     **197 B** |
| **RenderScene** | **raster**          | **DrawAtlas**            |   **2,967.98 μs** |  **14.734 μs** |  **12.304 μs** |    **6469 B** |
| **RenderScene** | **raster**          | **DrawPoints**           |   **8,361.24 μs** | **118.031 μs** | **157.568 μs** |    **2600 B** |
| **RenderScene** | **raster**          | **DrawVertices**         |   **5,517.95 μs** |  **38.985 μs** |  **34.559 μs** |    **6828 B** |
| **RenderScene** | **raster**          | **FilledCircle**         |     **134.33 μs** |   **1.132 μs** |   **1.059 μs** |      **89 B** |
| **RenderScene** | **raster**          | **GradientBlend**        |   **4,431.98 μs** |  **30.172 μs** |  **26.747 μs** |     **402 B** |
| **RenderScene** | **raster**          | **ImageFilterChain**     |  **76,721.06 μs** | **729.014 μs** | **681.921 μs** |    **1464 B** |
| **RenderScene** | **raster**          | **LargeImage**           |  **23,088.28 μs** | **127.487 μs** | **119.251 μs** |     **168 B** |
| **RenderScene** | **raster**          | **OpacityLayers**        |  **34,217.83 μs** | **258.117 μs** | **215.539 μs** |    **1170 B** |
| **RenderScene** | **raster**          | **PictureCache**         |   **4,690.93 μs** |  **39.893 μs** |  **37.316 μs** |      **44 B** |
| **RenderScene** | **raster**          | **RadialSweepGradient**  |   **4,157.15 μs** |  **53.117 μs** |  **49.686 μs** |    **1316 B** |
| **RenderScene** | **raster**          | **RedRo(...)White [21]** |      **75.95 μs** |   **0.411 μs** |   **0.364 μs** |      **89 B** |
| **RenderScene** | **raster**          | **RRectBlur**            |   **6,240.91 μs** |  **26.795 μs** |  **22.375 μs** |     **324 B** |
| **RenderScene** | **raster**          | **ShaderMask**           |   **9,977.65 μs** |  **72.595 μs** |  **67.905 μs** |     **444 B** |
| **RenderScene** | **raster**          | **SuperellipseBlur**     |   **5,931.96 μs** |  **99.847 μs** | **155.450 μs** |     **324 B** |
| **RenderScene** | **raster**          | **Text**                 |   **1,381.55 μs** |  **21.171 μs** |  **29.679 μs** |    **1091 B** |
