#!/usr/bin/env bash
# Installe rcodesign (https://github.com/indygreg/apple-platform-rs), outil libre qui signe « ad hoc »
# les applications macOS depuis Linux. Cela permet de préparer LocalConvert.app sans machine macOS.
set -euo pipefail
version=0.29.0
dir="${RUNNER_TEMP:-/tmp}/rcodesign"
mkdir -p "$dir"
curl -fsSL "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F$version/apple-codesign-$version-x86_64-unknown-linux-musl.tar.gz" \
  | tar -xz -C "$dir" --strip-components=1
"$dir/rcodesign" --version
if [ -n "${GITHUB_PATH:-}" ]; then echo "$dir" >> "$GITHUB_PATH"; fi
