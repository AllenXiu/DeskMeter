#!/bin/bash
# DeskMeter macOS .app 打包（本机或 CI macos runner 使用）
# 用法: VERSION=0.1.0 ./scripts/package-macos.sh   (可选 RID=osx-x64 交叉 Intel)
set -euo pipefail
cd "$(dirname "$0")/.."

RID="${RID:-osx-arm64}"
VERSION="${VERSION:-0.1.0}"
export DOTNET_ROOT="${DOTNET_ROOT:-/tmp/dotnet-sdk}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

rm -rf publish dist
echo "== dotnet publish ($RID, self-contained) =="
dotnet publish src/DeskMeter.App.Mac/DeskMeter.App.Mac.csproj -c Release -r "$RID" \
  --self-contained true -p:Version="$VERSION" -o publish

APP="dist/DeskMeter.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R publish/* "$APP/Contents/MacOS/"
cp -R publish/samples "$APP/Contents/Resources/" 2>/dev/null || true

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>DeskMeter</string>
  <key>CFBundleDisplayName</key><string>DeskMeter</string>
  <key>CFBundleIdentifier</key><string>io.deskmeter.app</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>DeskMeterMac</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/DeskMeterMac"
if command -v codesign >/dev/null 2>&1; then
  echo "== ad-hoc codesign =="
  codesign --force --deep -s - "$APP"
fi

echo "== zip =="
rm -f "DeskMeter-$VERSION-$RID.zip"
cd dist && zip -qry "../DeskMeter-$VERSION-$RID.zip" DeskMeter.app && cd ..
echo "done: DeskMeter-$VERSION-$RID.zip"
