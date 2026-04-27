DirectoryPath ROOT_PATH = MakeAbsolute(Directory("../.."));
DirectoryPath OUTPUT_PATH = MakeAbsolute(ROOT_PATH.Combine("output/native"));

#load "../../scripts/cake/native-shared.cake"

string SUPPORT_GPU_VAR = Argument("supportGpu", EnvironmentVariable("SUPPORT_GPU") ?? "true").ToLower();
bool SUPPORT_GPU = SUPPORT_GPU_VAR == "1" || SUPPORT_GPU_VAR == "true";

string SUPPORT_VULKAN_VAR = Argument("supportVulkan", EnvironmentVariable("SUPPORT_VULKAN") ?? "true");
bool SUPPORT_VULKAN = SUPPORT_VULKAN_VAR == "1" || SUPPORT_VULKAN_VAR.ToLower() == "true";

var VERIFY_EXCLUDED = Argument("verifyExcluded", Argument("verifyexcluded", ""))
    .ToLower().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

var VERIFY_INCLUDED = Argument("verifyIncluded", Argument("verifyincluded", ""))
    .ToLower().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

var VERIFY_GLIBC_MAX = Argument("verifyGlibcMax", Argument("verifyglibcmax", ""));

string CC = Argument("cc", EnvironmentVariable("CC"));
string CXX = Argument("cxx", EnvironmentVariable("CXX"));
string AR = Argument("ar", EnvironmentVariable("AR"));

string GetSkiaArch(string arch) =>
    arch switch {
        // Skia's GN files use "loong64", while the rest of our build and packaging
        // pipeline uses the more explicit "loongarch64".
        "loongarch64" => "loong64",
        _ => arch,
    };

string VARIANT = string.IsNullOrEmpty(BUILD_VARIANT) ? "linux" : BUILD_VARIANT?.Trim();

if (BUILD_ARCH.Length == 0)
    BUILD_ARCH = new [] { "x64" };

var COMPILERS = "";
if (!string.IsNullOrEmpty(CC))
    COMPILERS += $"cc='{CC}' ";
if (!string.IsNullOrEmpty(CXX))
    COMPILERS += $"cxx='{CXX}' ";
if (!string.IsNullOrEmpty(AR))
    COMPILERS += $"ar='{AR}' ";

void CheckDeps(FilePath so, bool checkIncluded = true)
{
    CheckLinuxDependencies(
        so,
        excluded: VERIFY_EXCLUDED,
        included: checkIncluded ? VERIFY_INCLUDED : null,
        maxGlibc: string.IsNullOrEmpty(VERIFY_GLIBC_MAX) ? null : VERIFY_GLIBC_MAX);
}

Task("libSkiaSharp")
    .IsDependentOn("git-sync-deps")
    .WithCriteria(IsRunningOnLinux())
    .Does(() =>
{
    // patch the gclient_paths.py for Python 3.7
    {
        var gclient = DEPOT_PATH.CombineWithFilePath("gclient_paths.py");
        var contents = System.IO.File.ReadAllText(gclient.FullPath);
        var newContents = contents
            .Replace("@functools.lru_cache", "@functools.lru_cache()")
            .Replace("@functools.lru_cache()()", "@functools.lru_cache()");
        if (contents != newContents)
            System.IO.File.WriteAllText(gclient.FullPath, newContents);
    }

    foreach (var arch in BUILD_ARCH) {
        if (Skip(arch)) return;

        var skiaArch = GetSkiaArch(arch);

        var soname = GetVersion("libSkiaSharp", "soname");
        var map = MakeAbsolute((FilePath)"libSkiaSharp/libSkiaSharp.map");

        // This is terrible! But, Alpine (musl) does not define this
        // so we are forced to for dng_sdk. If this ever becomes a problem
        // for other libraries, we will need to find a better solution.
        var wordSize = ReduceArch(arch).EndsWith("64") ? "64" : "32";
        var wordSizeDefine = VARIANT.ToLower().StartsWith("alpine")
            ? $", '-D__WORDSIZE={wordSize}'"
            : $"";

        // Architecture-specific Spectre mitigation flags
        // -mretpoline requires Clang; -mharden-sls=all works with both GCC and Clang
        var spectreFlags = arch switch {
            "x64" or "x86" => ", '-mretpoline'",
            "arm" or "arm64" => ", '-mharden-sls=all'",
            _ => ""  // RISC-V, LoongArch - no standard flags yet
        };

        // Bionic (Android NDK) builds need SK_BUILD_FOR_UNIX to prevent the
        // NDK's __ANDROID__ define from suppressing SkDebugf (stdio port).
        // Fontconfig is not available on Bionic.
        var isBionic = VARIANT.ToLower().StartsWith("bionic");
        var bionicDefine = isBionic ? ", '-DSK_BUILD_FOR_UNIX'" : "";
        var bionicArgs = isBionic ? "skia_use_fontconfig=false " : "";

        // Most variants build with a pure clang/LLVM runtime stack:
        //   -stdlib=libc++       libc++ instead of libstdc++
        //   -rtlib=compiler-rt   compiler-rt builtins instead of libgcc (this
        //                        provides __mulodi4 for 64-bit signed-mul-with-
        //                        overflow on 32-bit ABIs, emitted by dng_sdk's
        //                        __builtin_smul_overflow uses post-m133)
        //   -unwindlib=libunwind LLVM libunwind instead of libgcc_s
        //   -static-libstdc++    statically link libc++ into the .so
        //   -static-libgcc       in clang's driver this also flips libunwind
        //                        to its static archive (-l:libunwind.a)
        // Net effect: libSkiaSharp.so depends only on libc/libm/libdl/ld-linux.
        // The Android NDK already configures all of this by default, so adding
        // the flags is a no-op on bionic; the debian-cross image installs the
        // matching libc++/libunwind/compiler-rt multiarch packages.
        //
        // Two carve-outs stay on the historical `-static-libstdc++ -static-
        // libgcc` GCC stack:
        //
        //   * riscv64 — Bullseye main has no riscv64 binary archive, so the
        //     LLVM runtime libs aren't multiarch-installable. The debian/11
        //     image falls back to gcc-N-cross packages
        //     (libstdc++-10-dev-riscv64-cross + libgcc-10-dev-riscv64-cross).
        //   * alpine — Alpine ships libc++abi.a built without -fPIC (uses
        //     R_ARM_TLS_LE32 / equivalent local-exec TLS relocations), so it
        //     can't be statically linked into a -shared output. The libc++
        //     and libc++abi static archives also aren't merged the way
        //     Ubuntu/Debian's libc++.a is, so a static libc++ link leaves
        //     every __cxa_throw / __gxx_personality_v0 unresolved.
        //
        // 64-bit `__builtin_smul_overflow` on riscv64 doesn't lower to
        // `__mulodi4`, and alpine ships its own libgcc with the helper
        // available, so the post-m133 dng_sdk link issue doesn't apply on
        // either carve-out.
        var isRiscv64 = arch == "riscv64";
        var isAlpine = VARIANT.ToLower().StartsWith("alpine");
        var useLlvmRuntime = !isRiscv64 && !isAlpine;
        var llvmRuntimeCflags = useLlvmRuntime ? ", '-stdlib=libc++'" : "";
        var llvmRuntimeLdflags = useLlvmRuntime
            ? ", '-stdlib=libc++', '-rtlib=compiler-rt', '-unwindlib=libunwind'"
            : "";

        GnNinja($"{VARIANT}/{arch}", "SkiaSharp",
            $"target_os='linux' " +
            $"target_cpu='{skiaArch}' " +
            $"skia_enable_ganesh={(SUPPORT_GPU ? "true" : "false")} " +
            $"skia_use_harfbuzz=false " +
            $"skia_use_icu=false " +
            $"skia_use_piex=true " +
            $"skia_use_system_expat=false " +
            $"skia_use_system_freetype2=false " +
            $"skia_use_system_libjpeg_turbo=false " +
            $"skia_use_system_libpng=false " +
            $"skia_use_system_libwebp=false " +
            $"skia_use_system_zlib=false " +
            $"skia_enable_skottie=true " +
            $"skia_use_vulkan={SUPPORT_VULKAN} ".ToLower() +
            bionicArgs +
            $"extra_asmflags=[] " +
            $"extra_cflags=[ '-DSKIA_C_DLL', '-DHAVE_SYSCALL_GETRANDOM', '-DXML_DEV_URANDOM'{spectreFlags}{wordSizeDefine}{bionicDefine}{llvmRuntimeCflags} ] " +
            $"extra_ldflags=[ '-static-libstdc++', '-static-libgcc'{llvmRuntimeLdflags}, '-Wl,--version-script={map}' ] " +
            COMPILERS +
            $"linux_soname_version='{soname}' " +
            ADDITIONAL_GN_ARGS);

        var outDir = OUTPUT_PATH.Combine($"{VARIANT}/{arch}");
        EnsureDirectoryExists(outDir);
        var so = SKIA_PATH.CombineWithFilePath($"out/{VARIANT}/{arch}/libSkiaSharp.so.{soname}");
        CopyFileToDirectory(so, outDir);
        CopyFile(so, outDir.CombineWithFilePath("libSkiaSharp.so"));

        CheckDeps(so);
    }
});

Task("libHarfBuzzSharp")
    .IsDependentOn("git-sync-deps")
    .WithCriteria(IsRunningOnLinux())
    .Does(() =>
{
    foreach (var arch in BUILD_ARCH) {
        if (Skip(arch)) return;

        var skiaArch = GetSkiaArch(arch);

        var soname = GetVersion("HarfBuzz", "soname");
        var map = MakeAbsolute((FilePath)"libHarfBuzzSharp/libHarfBuzzSharp.map");

        // Match libSkiaSharp's clang/LLVM runtime stack on every variant
        // except the riscv64 + alpine carve-outs — see libSkiaSharp task
        // above for rationale.
        var hbIsRiscv64 = arch == "riscv64";
        var hbIsAlpine = VARIANT.ToLower().StartsWith("alpine");
        var hbUseLlvmRuntime = !hbIsRiscv64 && !hbIsAlpine;
        var hbBionicDefine = VARIANT.ToLower().StartsWith("bionic") ? "'-DSK_BUILD_FOR_UNIX'" : "";
        var hbLlvmRuntimeCflag = hbUseLlvmRuntime ? "'-stdlib=libc++'" : "";
        var hbExtraCflags = string.Join(", ", new[] { hbBionicDefine, hbLlvmRuntimeCflag }.Where(s => !string.IsNullOrEmpty(s)));
        var hbLlvmRuntimeLdflags = hbUseLlvmRuntime ? ", '-stdlib=libc++', '-rtlib=compiler-rt', '-unwindlib=libunwind'" : "";

        GnNinja($"{VARIANT}/{arch}", "HarfBuzzSharp",
            $"target_os='linux' " +
            $"target_cpu='{skiaArch}' " +
            $"visibility_hidden=false " +
            $"extra_asmflags=[] " +
            $"extra_cflags=[ {hbExtraCflags} ] " +
            $"extra_ldflags=[ '-static-libstdc++', '-static-libgcc'{hbLlvmRuntimeLdflags}, '-Wl,--version-script={map}' ] " +
            COMPILERS +
            $"linux_soname_version='{soname}' " +
            ADDITIONAL_GN_ARGS);

        var outDir = OUTPUT_PATH.Combine($"{VARIANT}/{arch}");
        EnsureDirectoryExists(outDir);
        var so = SKIA_PATH.CombineWithFilePath($"out/{VARIANT}/{arch}/libHarfBuzzSharp.so.{soname}");
        CopyFileToDirectory(so, outDir);
        CopyFile(so, outDir.CombineWithFilePath("libHarfBuzzSharp.so"));

        CheckDeps(so, checkIncluded: false); // HarfBuzz doesn't need fontconfig
    }
});

Task("Default")
    .IsDependentOn("libSkiaSharp")
    .IsDependentOn("libHarfBuzzSharp");

RunTarget(TARGET);
