# Arguments:
#   DOTNET_SDK_VERSION   - the version of dotnet for the Cake script        [ 10.0 | * ]
#   LLVM_VERSION         - the version of the LLVM compiler                 [ 19 | * ]
#   TOOLCHAIN_ARCH       - the multiarch triple                             [ arm-linux-gnueabihf | aarch64-linux-gnu | i386-linux-gnu | loongarch64-linux-gnu | riscv64-linux-gnu | x86_64-linux-gnu ]
#   TOOLCHAIN_ARCH_SHORT - the dpkg/Debian short architecture               [ armhf | arm64 | i386 | loong64 | riscv64 | amd64 ]
#   TOOLCHAIN_ARCH_TARGET - the LLVM target triple passed to clang          [ armv7a-linux-gnueabihf | aarch64-linux-gnu | i686-linux-gnu | loongarch64-linux-gnu | riscv64-linux-gnu | x86_64-linux-gnu ]
#
# Toolchain: clang/LLVM end-to-end. Cross-target runtime libraries (libc,
# compiler-rt builtins, libc++/libc++abi, libunwind) come from Debian's
# multiarch packages installed for the target. No GCC cross packages.

# Use --platform flag when building to select architecture
# NOTE: Using devcontainers base image because mcr.microsoft.com/mirror/docker/library/debian:13
# is not yet available. This is a Debian 13 (Trixie) image hosted on MCR.
FROM mcr.microsoft.com/devcontainers/base:debian13

USER root

# Set the architecture-specific variables based on the value of the BUILD_ARCH argument
ARG BUILD_ARCH=arm64
RUN case ${BUILD_ARCH} in \
        arm)         TOOLCHAIN_ARCH=arm-linux-gnueabihf       ; TOOLCHAIN_ARCH_SHORT=armhf   ; TOOLCHAIN_ARCH_TARGET=armv7a-linux-gnueabihf     ;; \
        arm64)       TOOLCHAIN_ARCH=aarch64-linux-gnu         ; TOOLCHAIN_ARCH_SHORT=arm64   ; TOOLCHAIN_ARCH_TARGET=aarch64-linux-gnu          ;; \
        loongarch64) TOOLCHAIN_ARCH=loongarch64-linux-gnu     ; TOOLCHAIN_ARCH_SHORT=loong64 ; TOOLCHAIN_ARCH_TARGET=loongarch64-linux-gnu      ;; \
        riscv64)     TOOLCHAIN_ARCH=riscv64-linux-gnu         ; TOOLCHAIN_ARCH_SHORT=riscv64 ; TOOLCHAIN_ARCH_TARGET=riscv64-linux-gnu          ;; \
        x86)         TOOLCHAIN_ARCH=i386-linux-gnu            ; TOOLCHAIN_ARCH_SHORT=i386    ; TOOLCHAIN_ARCH_TARGET=i686-linux-gnu             ;; \
        x64)         TOOLCHAIN_ARCH=x86_64-linux-gnu          ; TOOLCHAIN_ARCH_SHORT=amd64   ; TOOLCHAIN_ARCH_TARGET=x86_64-linux-gnu           ;; \
        *) echo "Unsupported architecture: ${BUILD_ARCH}" && exit 1 ;; \
    esac \
    && echo "export TOOLCHAIN_ARCH=${TOOLCHAIN_ARCH}" > /etc/skia-env \
    && echo "export TOOLCHAIN_ARCH_SHORT=${TOOLCHAIN_ARCH_SHORT}" >> /etc/skia-env \
    && echo "export TOOLCHAIN_ARCH_TARGET=${TOOLCHAIN_ARCH_TARGET}" >> /etc/skia-env

# Enable the target architecture for multiarch installs (no-op for amd64).
RUN . /etc/skia-env \
    && dpkg --add-architecture ${TOOLCHAIN_ARCH_SHORT}

# Install the host build tools.
ARG LLVM_VERSION=19
RUN apt-get update \
    && apt-get install -y \
        curl python3 git ninja-build xz-utils \
        clang-${LLVM_VERSION} lld-${LLVM_VERSION} \
    && rm -rf /var/lib/apt/lists/*

# Install the cross-target runtime libraries via Debian multiarch:
#   libc6-dev:<arch>            target glibc + headers
#   libclang-rt-19-dev:<arch>   compiler-rt builtins + crt files (replaces libgcc)
#   libc++-19-dev:<arch>        libc++ headers + static archive (replaces libstdc++)
#   libc++abi-19-dev:<arch>     libc++abi static archive
#   libunwind-19-dev:<arch>     LLVM libunwind static archive (replaces libgcc_s)
#   libfontconfig1-dev:<arch>   fontconfig headers + library (Skia dep)
RUN . /etc/skia-env \
    && apt-get update \
    && apt-get install -y \
        libc6-dev:${TOOLCHAIN_ARCH_SHORT} \
        libclang-rt-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
        libc++-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
        libc++abi-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
        libunwind-${LLVM_VERSION}-dev:${TOOLCHAIN_ARCH_SHORT} \
        libfontconfig1-dev:${TOOLCHAIN_ARCH_SHORT} \
    && rm -rf /var/lib/apt/lists/*

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
