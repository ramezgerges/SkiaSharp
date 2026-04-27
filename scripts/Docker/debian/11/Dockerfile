# Arguments:
#   DOTNET_SDK_VERSION   - the version of dotnet for the Cake script        [ 10.0 | * ]
#   LLVM_VERSION         - the version of the LLVM compiler                 [ 13 | * ]
#   TOOLCHAIN_VERSION    - the version of the GCC toolchain (riscv64 only)  [ 10 | * ]
#   TOOLCHAIN_ARCH       - the multiarch triple                             [ arm-linux-gnueabihf | aarch64-linux-gnu | i386-linux-gnu | riscv64-linux-gnu | x86_64-linux-gnu ]
#   TOOLCHAIN_ARCH_SHORT - the dpkg/Debian short architecture               [ armhf | arm64 | i386 | riscv64 | amd64 ]
#   TOOLCHAIN_ARCH_TARGET - the LLVM target triple passed to clang          [ armv7a-linux-gnueabihf | aarch64-linux-gnu | i686-linux-gnu | riscv64-linux-gnu | x86_64-linux-gnu ]
#
# Toolchain: clang/LLVM end-to-end for arm/arm64/x86/x64 — cross-target
# runtime libraries (libc, compiler-rt builtins, libc++/libc++abi,
# libunwind) come from Debian's multiarch packages.
#
# riscv64 is a carve-out: Bullseye main has no riscv64 binary archive
# (riscv64 only became a Debian release arch in Trixie), so multiarch
# can't install LLVM runtime libraries for it. Instead riscv64 uses
# the gcc-N-cross packages (built from the cross-toolchain-base source
# package, shipped as amd64 .debs containing a riscv64 sysroot) and the
# build links against libstdc++/libgcc statically. This keeps glibc 2.31
# parity with the other archs at the cost of not using the LLVM runtime
# stack on this one target.

# Use --platform flag when building to select architecture
FROM mcr.microsoft.com/mirror/docker/library/debian:11

# Set the architecture-specific variables based on the value of the BUILD_ARCH argument
ARG BUILD_ARCH=arm64
RUN case ${BUILD_ARCH} in \
        arm)         TOOLCHAIN_ARCH=arm-linux-gnueabihf       ; TOOLCHAIN_ARCH_SHORT=armhf   ; TOOLCHAIN_ARCH_TARGET=armv7a-linux-gnueabihf     ;; \
        arm64)       TOOLCHAIN_ARCH=aarch64-linux-gnu         ; TOOLCHAIN_ARCH_SHORT=arm64   ; TOOLCHAIN_ARCH_TARGET=aarch64-linux-gnu          ;; \
        riscv64)     TOOLCHAIN_ARCH=riscv64-linux-gnu         ; TOOLCHAIN_ARCH_SHORT=riscv64 ; TOOLCHAIN_ARCH_TARGET=riscv64-linux-gnu          ;; \
        x86)         TOOLCHAIN_ARCH=i386-linux-gnu            ; TOOLCHAIN_ARCH_SHORT=i386    ; TOOLCHAIN_ARCH_TARGET=i686-linux-gnu             ;; \
        x64)         TOOLCHAIN_ARCH=x86_64-linux-gnu          ; TOOLCHAIN_ARCH_SHORT=amd64   ; TOOLCHAIN_ARCH_TARGET=x86_64-linux-gnu           ;; \
        *) echo "Unsupported architecture: ${BUILD_ARCH}" && exit 1 ;; \
    esac \
    && echo "export TOOLCHAIN_ARCH=${TOOLCHAIN_ARCH}" > /etc/skia-env \
    && echo "export TOOLCHAIN_ARCH_SHORT=${TOOLCHAIN_ARCH_SHORT}" >> /etc/skia-env \
    && echo "export TOOLCHAIN_ARCH_TARGET=${TOOLCHAIN_ARCH_TARGET}" >> /etc/skia-env

# Enable the target architecture for multiarch installs (only the LLVM
# multiarch path; riscv64 has no Bullseye binary archive so skip it).
RUN . /etc/skia-env \
    && if [ "${TOOLCHAIN_ARCH_SHORT}" != "riscv64" ]; then \
           dpkg --add-architecture ${TOOLCHAIN_ARCH_SHORT}; \
       fi

# Install the host build tools.
# clang-13 (rather than 11) — clang-11's bundled mm_malloc.h declares
# posix_memalign() without `throw()` while the cross-target glibc
# stdlib.h declares it with `throw()`. C++ requires matching exception
# specs across redeclarations, so any TU that pulls in <immintrin.h>
# (e.g. modules/skcms/skcms.cc on x86/x64 with SSE) fails to compile
# with clang-11. Fixed upstream in clang-12+ which added the throw()
# spec to the intrinsics header.
ARG LLVM_VERSION=13
RUN apt-get update \
    && apt-get install -y \
        curl python3 git ninja-build xz-utils \
        clang-${LLVM_VERSION} lld-${LLVM_VERSION} \
    && rm -rf /var/lib/apt/lists/*

# Install the cross-target runtime libraries.
#
# arm/arm64/x86/x64 (LLVM multiarch path):
#   libc6-dev:<arch>            target glibc + headers
#   libclang-rt-13-dev:<arch>   compiler-rt builtins + crt files
#   libc++-13-dev:<arch>        libc++ headers + static archive
#   libc++abi-13-dev:<arch>     libc++abi static archive
#   libunwind-13-dev:<arch>     LLVM libunwind static archive
#   libfontconfig1-dev:<arch>   fontconfig headers + library
#
# riscv64 (gcc-cross path — see file header for rationale):
#   libstdc++-10-dev-riscv64-cross  target glibc + libstdc++ + libgcc sysroot
#                                   under /usr/riscv64-linux-gnu/, glibc 2.31
#   binutils-riscv64-linux-gnu      cross-arch ld/as
#   libfontconfig is fetched from the Trixie pool below (Bullseye has no
#   riscv64 .debs in the regular archive)
ARG TOOLCHAIN_VERSION=10
RUN . /etc/skia-env \
    && apt-get update \
    && if [ "${TOOLCHAIN_ARCH_SHORT}" = "riscv64" ]; then \
           apt-get install -y \
               libstdc++-${TOOLCHAIN_VERSION}-dev-${TOOLCHAIN_ARCH_SHORT}-cross \
               libgcc-${TOOLCHAIN_VERSION}-dev-${TOOLCHAIN_ARCH_SHORT}-cross \
               binutils-${TOOLCHAIN_ARCH}; \
       else \
           apt-get install -y \
               libc6-dev:${TOOLCHAIN_ARCH_SHORT} \
               libclang-rt-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
               libc++-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
               libc++abi-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
               libunwind-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
               libfontconfig1-dev:${TOOLCHAIN_ARCH_SHORT}; \
       fi \
    && rm -rf /var/lib/apt/lists/*

# riscv64-only: native/linux-clang-cross/build.cake adds
# `-I/usr/<triple>/include/c++/current` to the GN cflags so the
# version-specific libstdc++ headers can be referenced without baking
# the toolchain version into the build script. Create that symlink
# pointing at the gcc-N-cross libstdc++ headers, and strip absolute
# `/usr/<triple>/lib/` prefixes from the libc / libpthread linker
# scripts so they resolve relative to the cross sysroot at link time.
RUN . /etc/skia-env \
    && if [ "${TOOLCHAIN_ARCH_SHORT}" = "riscv64" ]; then \
           ln -s /usr/${TOOLCHAIN_ARCH}/include/c++/${TOOLCHAIN_VERSION} \
                 /usr/${TOOLCHAIN_ARCH}/include/c++/current \
           && sed -i "s|/usr/${TOOLCHAIN_ARCH}/lib/||g" /usr/${TOOLCHAIN_ARCH}/lib/libpthread.so \
           && sed -i "s|/usr/${TOOLCHAIN_ARCH}/lib/||g" /usr/${TOOLCHAIN_ARCH}/lib/libc.so; \
       fi

# riscv64-only: pull libfontconfig from Trixie's pool — Bullseye main
# has no riscv64 .debs and the pool/ tree is suite-agnostic, so a
# Trixie-built libfontconfig.so.1 + headers drop in cleanly. The .so
# is dynamic-linked at runtime against whatever fontconfig the user's
# system ships; we only need symbols/ABI here.
RUN . /etc/skia-env \
    && if [ "${TOOLCHAIN_ARCH_SHORT}" = "riscv64" ]; then \
           mkdir -p /skia-utils/libfontconfig-dev \
           && cd /skia-utils/libfontconfig-dev \
           && FC_VERSION=2.15.0-2.3 \
           && APT_REPO=http://deb.debian.org/debian \
           && for pkg in libfontconfig-dev libfontconfig1; do \
                  url="${APT_REPO}/pool/main/f/fontconfig/${pkg}_${FC_VERSION}_${TOOLCHAIN_ARCH_SHORT}.deb"; \
                  echo "Downloading $url"; \
                  curl -L -o "${pkg}.deb" "$url"; \
                  ar x "${pkg}.deb" data.tar.xz \
                  && tar -xf data.tar.xz \
                  && cp -R usr/lib/*/* /usr/${TOOLCHAIN_ARCH}/lib/ \
                  && { [ -d usr/include ] && cp -R usr/include/* /usr/${TOOLCHAIN_ARCH}/include/ || true; } \
                  && rm -rf *.deb *.tar* debian-binary usr; \
              done; \
       fi

# Install the .NET SDK
ARG DOTNET_SDK_VERSION=10.0.1xx
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
RUN curl https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -L -o dotnet-install.sh \
    && bash dotnet-install.sh --channel ${DOTNET_SDK_VERSION} --install-dir /usr/share/dotnet --verbose \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm dotnet-install.sh \
    && dotnet help \
    && dotnet --info

ENV CC=clang-${LLVM_VERSION} CXX=clang++-${LLVM_VERSION}

WORKDIR /work

COPY ./startup.sh /
RUN chmod +x /startup.sh
ENTRYPOINT [ "/startup.sh" ]
