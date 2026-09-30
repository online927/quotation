#!/usr/bin/env bash
# Cross-publishes the Windows binaries from Linux/macOS (installers are built on Windows / CI).
set -euo pipefail
VERSION="${1:-0.9.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/publish"
rm -rf "$OUT"
COMMON=(-c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
        -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none "-p:Version=$VERSION")
dotnet publish "$ROOT/src/Quotation.Server" "${COMMON[@]}" -o "$OUT/server"
dotnet publish "$ROOT/src/Quotation.Desktop" "${COMMON[@]}" -o "$OUT/desktop"
dotnet publish "$ROOT/tools/Tally.Simulator" "${COMMON[@]}" -o "$OUT/simulator"
echo "Windows binaries written to $OUT"
