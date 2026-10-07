#!/bin/zsh
set -euo pipefail

SOURCE="${0:A:h}"
WORK="${SPEECH_BUILD_ROOT:?Set SPEECH_BUILD_ROOT to an external temporary build directory}"
RUN="$WORK/build-$(date +%Y%m%d-%H%M%S)"
SIGNING_IDENTITY="${SPEECH_SIGNING_IDENTITY:?A Developer ID signing identity is required}"
NOTARY_PROFILE="${SPEECH_NOTARY_PROFILE:?A notarytool keychain profile is required}"
NOTARY_KEYCHAIN="${SPEECH_NOTARY_KEYCHAIN:-$HOME/Library/Keychains/login.keychain-db}"
ARCH="${SPEECH_ARCH:-arm64}"
case "$ARCH" in
arm64) TARGET_FLAGS=(); PACKAGE_ARCH="" ;;
x86_64) TARGET_FLAGS=(--triple x86_64-apple-macosx14.0); PACKAGE_ARCH="-Intel" ;;
  *) print -u2 "Unsupported Mac architecture: $ARCH"; exit 2 ;;
esac
mkdir -p "$RUN/tmp" "$RUN/stage"
export TMPDIR="$RUN/tmp/"
cd "$SOURCE"

security find-identity -v -p codesigning | grep -Fq '"'"$SIGNING_IDENTITY"'"'
xcrun notarytool history --keychain-profile "$NOTARY_PROFILE" --keychain "$NOTARY_KEYCHAIN" >/dev/null

if [[ "$ARCH" == "x86_64" ]]; then
  arch -x86_64 /usr/bin/swift test --build-system native --disable-swift-testing --build-path "$RUN/swift-build" "${TARGET_FLAGS[@]}"
else
  swift test --build-system native --build-path "$RUN/swift-build"
fi
swift build --build-system native -c release --build-path "$RUN/swift-build" "${TARGET_FLAGS[@]}"
BIN_DIR=$(swift build --build-system native -c release --show-bin-path --build-path "$RUN/swift-build" "${TARGET_FLAGS[@]}")

APP="$RUN/stage/ElevenLabs Speech Generator.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/ElevenLabsSpeechGeneratorMac" "$APP/Contents/MacOS/"
cp "$SOURCE/Info.plist" "$APP/Contents/Info.plist"
cp "$SOURCE/Manual.html" "$APP/Contents/Resources/Manual.html"
cp "$SOURCE/LICENSE.txt" "$APP/Contents/Resources/LICENSE.txt"
codesign --force --options runtime --timestamp --sign "$SIGNING_IDENTITY" "$APP"
codesign --verify --deep --strict "$APP"

VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$SOURCE/Info.plist")
ZIP="$RUN/ElevenLabs-Speech-Generator-Mac${PACKAGE_ARCH}-$VERSION.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --keychain "$NOTARY_KEYCHAIN" --wait
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
printf 'APP=%s\nZIP=%s\n' "$APP" "$ZIP"
