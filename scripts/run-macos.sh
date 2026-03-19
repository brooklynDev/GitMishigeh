#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/GitMishigeh/GitMishigeh.csproj"
CONFIGURATION="Debug"
FRAMEWORK="net10.0"
BUILD_DIR="$ROOT_DIR/GitMishigeh/bin/$CONFIGURATION/$FRAMEWORK"
APP_BUNDLE_DIR="$ROOT_DIR/artifacts/macos-dev/GitMishigeh.app"
MACOS_DIR="$APP_BUNDLE_DIR/Contents/MacOS"
RESOURCES_DIR="$APP_BUNDLE_DIR/Contents/Resources"
PLIST_PATH="$APP_BUNDLE_DIR/Contents/Info.plist"
APP_EXECUTABLE_NAME="GitMishigeh"
APP_DISPLAY_NAME="GitMishigeh"
ICON_SRC="$ROOT_DIR/GitMishigeh/Assets/git-mishigeh-1024.png"

mkdir -p "$ROOT_DIR/artifacts/macos-dev"

create_icns() {
  local output_icns="$1"
  local tmp_dir
  tmp_dir="$(mktemp -d)"
  local iconset_dir="$tmp_dir/app.iconset"
  mkdir -p "$iconset_dir"

  sips -z 16 16 "$ICON_SRC" --out "$iconset_dir/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ICON_SRC" --out "$iconset_dir/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ICON_SRC" --out "$iconset_dir/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ICON_SRC" --out "$iconset_dir/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ICON_SRC" --out "$iconset_dir/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ICON_SRC" --out "$iconset_dir/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ICON_SRC" --out "$iconset_dir/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ICON_SRC" --out "$iconset_dir/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ICON_SRC" --out "$iconset_dir/icon_512x512.png" >/dev/null
  cp "$ICON_SRC" "$iconset_dir/icon_512x512@2x.png"

  iconutil -c icns "$iconset_dir" -o "$output_icns"
  rm -rf "$tmp_dir"
}

echo "Building macOS debug app..."
dotnet build "$APP_PROJECT" -c "$CONFIGURATION" -p:UsedAvaloniaProducts=

rm -rf "$APP_BUNDLE_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

cp -R "$BUILD_DIR/." "$MACOS_DIR/"
chmod +x "$MACOS_DIR/$APP_EXECUTABLE_NAME"
create_icns "$RESOURCES_DIR/app-icon.icns"

cat > "$PLIST_PATH" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>com.internal.gitmishigeh.dev</string>
  <key>CFBundleVersion</key>
  <string>0.1.0-dev</string>
  <key>CFBundleShortVersionString</key>
  <string>0.1.0-dev</string>
  <key>CFBundleExecutable</key>
  <string>$APP_EXECUTABLE_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleIconFile</key>
  <string>app-icon.icns</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

echo "Launching $APP_BUNDLE_DIR"
open -W "$APP_BUNDLE_DIR"
