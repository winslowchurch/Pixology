#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "$0")" && pwd)"
RID="${1:-osx-arm64}"

case "$RID" in
    osx-arm64|osx-x64) ;;
    *)
        echo "Usage: $0 [osx-arm64|osx-x64]" >&2
        exit 2
        ;;
esac

PUBLISH_DIR="$ROOT_DIR/artifacts/publish-$RID"
ICONSET_DIR="$ROOT_DIR/artifacts/Pixology.iconset"
APP_DIR="$ROOT_DIR/dist/$RID/Pixology.app"
CONTENTS_DIR="$APP_DIR/Contents"

rm -rf "$PUBLISH_DIR" "$ICONSET_DIR" "$APP_DIR"
mkdir -p "$PUBLISH_DIR" "$ICONSET_DIR" "$CONTENTS_DIR/MacOS" "$CONTENTS_DIR/Resources"

dotnet publish "$ROOT_DIR/Pixology.csproj" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    --output "$PUBLISH_DIR"

ditto "$PUBLISH_DIR" "$CONTENTS_DIR/MacOS"

create_icon()
{
    local size="$1"
    local file_name="$2"
    sips -s format png -z "$size" "$size" "$ROOT_DIR/Icon.bmp" \
        --out "$ICONSET_DIR/$file_name" >/dev/null
}

create_icon 16 icon_16x16.png
create_icon 32 icon_16x16@2x.png
create_icon 32 icon_32x32.png
create_icon 64 icon_32x32@2x.png
create_icon 128 icon_128x128.png
create_icon 256 icon_128x128@2x.png
create_icon 256 icon_256x256.png
create_icon 512 icon_256x256@2x.png
create_icon 512 icon_512x512.png
create_icon 1024 icon_512x512@2x.png
iconutil --convert icns "$ICONSET_DIR" --output "$CONTENTS_DIR/Resources/Pixology.icns"

cat > "$CONTENTS_DIR/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>Pixology</string>
    <key>CFBundleIconFile</key>
    <string>Pixology</string>
    <key>CFBundleIdentifier</key>
    <string>com.winslowchurch.pixology</string>
    <key>CFBundleName</key>
    <string>Pixology</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

chmod +x "$CONTENTS_DIR/MacOS/Pixology"
echo "Created $APP_DIR"
echo "Move Pixology.app to /Applications to install it."
