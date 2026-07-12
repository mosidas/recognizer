# 言語非依存の汎用ベース（devcontainer 公式 base イメージ）。
# .NET / Python は devcontainer feature で導入する。
FROM mcr.microsoft.com/devcontainers/base:ubuntu

# --- 共通ツール: lazygit（gh は devcontainer feature で導入）---
RUN set -eux; \
    arch="$(uname -m)"; \
    case "$arch" in \
      x86_64) lg_arch=x86_64 ;; \
      aarch64|arm64) lg_arch=arm64 ;; \
      *) lg_arch=x86_64 ;; \
    esac; \
    LAZYGIT_VERSION="$(curl -fsSL https://api.github.com/repos/jesseduffield/lazygit/releases/latest | grep -Po '"tag_name": *"v\K[^"]*')"; \
    curl -fsSL -o /tmp/lazygit.tar.gz "https://github.com/jesseduffield/lazygit/releases/download/v${LAZYGIT_VERSION}/lazygit_${LAZYGIT_VERSION}_Linux_${lg_arch}.tar.gz"; \
    tar -xzf /tmp/lazygit.tar.gz -C /usr/local/bin lazygit; \
    rm -f /tmp/lazygit.tar.gz; \
    lazygit --version

# --- OpenCvSharp (libOpenCvSharpExtern.so) が動的リンクするネイティブライブラリ ---
# 不足があれば `ldd` で確認して追記する
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get install -y --no-install-recommends \
       ffmpeg \
       libtbb12 \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# --- Claude Code の設定永続化先（名前付きボリュームの所有者を vscode に合わせる）---
RUN mkdir -p /home/vscode/.claude && chown -R vscode:vscode /home/vscode/.claude
