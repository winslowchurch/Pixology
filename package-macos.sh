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
APP_DIR="$ROOT_DIR/dist/$RID/Pixology.app"
CONTENTS_DIR="$APP_DIR/Contents"

rm -rf "$PUBLISH_DIR" "$APP_DIR"
mkdir -p "$PUBLISH_DIR" "$CONTENTS_DIR/MacOS" "$CONTENTS_DIR/Resources"

dotnet publish "$ROOT_DIR/Pixology.csproj" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained false \
    --output "$PUBLISH_DIR"

ditto "$PUBLISH_DIR" "$CONTENTS_DIR/MacOS"

sips -s format icns "$ROOT_DIR/Icon.bmp" \
    --out "$CONTENTS_DIR/Resources/Pixology.icns" >/dev/null

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
