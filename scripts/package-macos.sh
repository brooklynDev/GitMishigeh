#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/GitMishigeh/GitMishigeh.csproj"
ICON_SRC="$ROOT_DIR/GitMishigeh/Assets/avalonia-logo.ico"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/macos"
APP_EXECUTABLE_NAME="GitMishigeh"
APP_DISPLAY_NAME="GitMishigeh"
BUNDLE_ID="com.internal.gitmishigeh"
VERSION="0.1.0"

mkdir -p "$ARTIFACTS_DIR"

create_icns() {
  local output_icns="$1"
  local tmp_dir
  tmp_dir="$(mktemp -d)"
  local iconset_dir="$tmp_dir/app.iconset"
  mkdir -p "$iconset_dir"

  magick "$ICON_SRC" -resize 16x16 "$iconset_dir/icon_16x16.png"
  magick "$ICON_SRC" -resize 32x32 "$iconset_dir/icon_16x16@2x.png"
  magick "$ICON_SRC" -resize 32x32 "$iconset_dir/icon_32x32.png"
  magick "$ICON_SRC" -resize 64x64 "$iconset_dir/icon_32x32@2x.png"
  magick "$ICON_SRC" -resize 128x128 "$iconset_dir/icon_128x128.png"
  magick "$ICON_SRC" -resize 256x256 "$iconset_dir/icon_128x128@2x.png"
  magick "$ICON_SRC" -resize 256x256 "$iconset_dir/icon_256x256.png"
  magick "$ICON_SRC" -resize 512x512 "$iconset_dir/icon_256x256@2x.png"
  magick "$ICON_SRC" -resize 512x512 "$iconset_dir/icon_512x512.png"
  magick "$ICON_SRC" -resize 1024x1024 "$iconset_dir/icon_512x512@2x.png"

  iconutil -c icns "$iconset_dir" -o "$output_icns" || {
    echo "Warning: iconutil failed, falling back to copied icon."
    cp "$ICON_SRC" "$output_icns"
  }

  rm -rf "$tmp_dir"
}

package_rid() {
  local rid="$1"
  local app_bundle_name
  local zip_name

  if [[ "$rid" == "osx-arm64" ]]; then
    app_bundle_name="GitMishigeh (Apple Silicon).app"
    zip_name="GitMishigeh-macOS-AppleSilicon.zip"
  else
    app_bundle_name="GitMishigeh (Intel).app"
    zip_name="GitMishigeh-macOS-Intel.zip"
  fi

  local publish_dir="$ROOT_DIR/GitMishigeh/bin/Release/net10.0/$rid/publish"
  local rid_out_dir="$ARTIFACTS_DIR/$rid"
  local app_bundle_path="$rid_out_dir/$app_bundle_name"
  local macos_dir="$app_bundle_path/Contents/MacOS"
  local resources_dir="$app_bundle_path/Contents/Resources"
  local plist_path="$app_bundle_path/Contents/Info.plist"
  local zip_path="$ARTIFACTS_DIR/$zip_name"

  rm -rf "$rid_out_dir" "$zip_path"
  mkdir -p "$macos_dir" "$resources_dir"

  echo "Publishing $rid..."
  dotnet publish "$APP_PROJECT" \
    -c Release \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:UseAppHost=true \
    -p:PublishTrimmed=false \
    -p:UsedAvaloniaProducts=

  cp -R "$publish_dir/." "$macos_dir/"
  chmod +x "$macos_dir/$APP_EXECUTABLE_NAME"

  create_icns "$resources_dir/app-icon.icns"

  cat > "$plist_path" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID.$rid</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
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

  ditto -c -k --sequesterRsrc --keepParent "$app_bundle_path" "$zip_path"
  echo "Created $zip_path"
}

package_rid "osx-arm64"
package_rid "osx-x64"
