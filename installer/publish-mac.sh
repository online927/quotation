#!/usr/bin/env bash
# Builds the Mac app (Quotations.app in a .dmg) for Apple Silicon and Intel Macs. Runs on a Mac (CI: macos-latest).
# The Mac app is the PC app; it connects to the Quotation Server on the Windows PC with Tally.
set -euo pipefail
VERSION="${1:-0.9.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/publish/mac"
rm -rf "$OUT"
mkdir -p "$OUT"

# The icon: AppIcon.icns from the 1024-pixel picture.
ICONSET="$OUT/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$ROOT/installer/mac/AppIcon.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z $double $double "$ROOT/installer/mac/AppIcon.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$OUT/AppIcon.icns"

for RID in osx-arm64 osx-x64; do
  NAME=$([ "$RID" = "osx-arm64" ] && echo "AppleSilicon" || echo "Intel")
  BIN="$OUT/bin-$RID"
  dotnet publish "$ROOT/src/Quotation.Desktop" -c Release -r "$RID" --self-contained \
    -p:DebugType=none -p:UseAppHost=true "-p:Version=$VERSION" -o "$BIN"
  APP="$OUT/$NAME/Quotations.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$BIN"/. "$APP/Contents/MacOS/"
  cp "$OUT/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
  sed "s/__VERSION__/$VERSION/g" "$ROOT/installer/mac/Info.plist" > "$APP/Contents/Info.plist"
  chmod +x "$APP/Contents/MacOS/TSQuotation"
  # Ad-hoc signature (Apple Silicon needs one to start; without a paid Apple developer account the first start is
  # right-click → Open, or System Settings → Privacy & Security → Open Anyway).
  codesign --force --deep --sign - "$APP"
  ln -s /Applications "$OUT/$NAME/Applications"
  hdiutil create -volname "Quotations" -srcfolder "$OUT/$NAME" -ov -format UDZO "$OUT/TSQuotation-Mac-$NAME-$VERSION.dmg"
done
mkdir -p "$ROOT/publish/mac-app"
mv "$OUT"/*.dmg "$ROOT/publish/mac-app/"
echo "Mac apps written to $ROOT/publish/mac-app"
