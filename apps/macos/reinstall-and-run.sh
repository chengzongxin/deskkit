#!/bin/sh
set -eu
cd "$(dirname "$0")"
APP_ID="app.deskkit.macos"
BIN_PATH="$PWD/.build/debug/DeskKit"
BUILD_APP="$PWD/build/DeskKit.app"
osascript -e 'tell application "DeskKit" to quit' 2>/dev/null || true
pkill -x DeskKit 2>/dev/null || true
sleep 0.4
./build-app.sh
open "$BUILD_APP"
echo "已安装并启动 $BUILD_APP"