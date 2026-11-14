# Mirrors the ubuntu-22.04 runner used by .github/workflows/build-desktop-linux.yml.
FROM ubuntu:22.04

ARG UID=1000
ARG GID=1000
ARG DOTNET_SDK
ARG NODE_MAJOR=22
ARG TAURI_DRIVER=2.0.6

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y \
        ca-certificates curl git build-essential pkg-config \
        libwebkit2gtk-4.1-dev \
        libgtk-3-dev \
        librsvg2-dev \
        libayatana-appindicator3-dev \
        libxdo-dev \
        libssl-dev \
        webkit2gtk-driver \
        xvfb \
        openbox \
        xdotool \
        x11-utils \
        dbus-x11 \
        sway \
        wtype \
        python3 \
        patchelf \
        xdg-utils \
        file \
        wget \
        libicu70 rpm libarchive-tools zstd unzip binutils \
    && curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Sway refuses to start when the host kernel has the Nvidia module loaded, even headless.
RUN printf '#!/bin/sh\nexec /usr/bin/sway --unsupported-gpu "$@"\n' > /usr/local/bin/sway \
    && chmod +x /usr/local/bin/sway

ENV DOTNET_ROOT=/opt/dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --version "$DOTNET_SDK" --install-dir /opt/dotnet \
    && rm /tmp/dotnet-install.sh

RUN groupadd -o -g "$GID" ci \
    && useradd -o -m -u "$UID" -g "$GID" -s /bin/bash ci \
    && mkdir -p /opt/rust && chown ci:ci /opt/rust

USER ci
ENV RUSTUP_HOME=/opt/rust/rustup CARGO_HOME=/opt/rust/cargo
ENV PATH=/opt/rust/cargo/bin:/opt/dotnet:$PATH
RUN curl -fsSL https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain stable --no-modify-path \
    && cargo install tauri-driver --version "$TAURI_DRIVER" --locked \
    && rm -rf /opt/rust/cargo/registry /opt/rust/cargo/git

# WebKit's bubblewrap sandbox can't create namespaces under Docker's default seccomp profile.
ENV CI=true WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1
WORKDIR /src
