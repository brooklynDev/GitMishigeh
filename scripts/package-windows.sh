#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/GitMishigeh/GitMishigeh.csproj"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/windows"
RID="win-x64"
APP_DIR_NAME="GitMishigeh-Windows-x64"
ZIP_NAME="GitMishigeh-Windows-x64.zip"

mkdir -p "$ARTIFACTS_DIR"

publish_dir="$ROOT_DIR/GitMishigeh/bin/Release/net10.0/$RID/publish"
rid_out_dir="$ARTIFACTS_DIR/$RID"
app_dir="$rid_out_dir/$APP_DIR_NAME"
zip_path="$ARTIFACTS_DIR/$ZIP_NAME"

rm -rf "$rid_out_dir" "$zip_path"
mkdir -p "$app_dir"

echo "Publishing $RID..."
dotnet publish "$APP_PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:UseAppHost=true \
  -p:PublishTrimmed=false \
  -p:UsedAvaloniaProducts=

cp -R "$publish_dir/." "$app_dir/"

(
  cd "$rid_out_dir"
  ditto -c -k --keepParent "$APP_DIR_NAME" "$zip_path"
)

echo "Created $zip_path"
