#!/bin/sh
set -eu
cd "$(dirname "$0")"
swift build
APP="build/DeskKit.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/debug/DeskKit "$APP/Contents/MacOS/DeskKit"
cp Info.plist "$APP/Contents/Info.plist"
cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
mkdir -p "$APP/Contents/Resources/contracts"
cp -R ../../contracts/. "$APP/Contents/Resources/contracts/"
CERT="DeskKit Local Code"
if security find-certificate -c "$CERT" >/dev/null 2>&1; then
  codesign --force --sign "$CERT" --identifier app.deskkit.macos "$APP"
else
  codesign --force --sign - --identifier app.deskkit.macos "$APP"
fi
echo "built $APP"
