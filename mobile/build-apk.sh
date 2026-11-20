#!/usr/bin/env bash
# Build the CMX Android app in Docker (see docs/VERSIONING.md).
#
#   mobile/build-apk.sh debug     -> mobile/out/CMX-<version>-debug.apk (debug-signed, for testing)
#   mobile/build-apk.sh release   -> mobile/out/CMX-<version>.apk (signed with the release key)
#
# Release builds need CMMS_KEYSTORE (path to the key file), CMMS_KEYSTORE_PASSWORD, CMMS_KEY_ALIAS and
# CMMS_KEY_PASSWORD; scripts/release.sh sets them from the private signing folder. The key and its passwords only
# exist inside the build container, never in the working tree.
set -euo pipefail

MODE=${1:-debug}
case "$MODE" in debug|release) ;; *) echo "usage: mobile/build-apk.sh [debug|release]" >&2; exit 1 ;; esac
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
VERSION=$(tr -d '[:space:]' < "$ROOT/VERSION")
IMAGE=cmms-android-build
export PATH="$PATH:$HOME/.docker/bin"

if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  echo "Building the Android build image (first time only, several minutes)..."
  docker build --platform linux/amd64 -t "$IMAGE" "$HERE"
fi
mkdir -p "$HERE/out"

args=(--rm --platform linux/amd64 -v "$ROOT":/repo:ro -v "$HERE/out":/out -v cmmsx-gradle:/root/.gradle -v cmmsx-npm-amd64:/root/.npm)
if [ "$MODE" = release ]; then
  for v in CMMS_KEYSTORE CMMS_KEYSTORE_PASSWORD CMMS_KEY_ALIAS CMMS_KEY_PASSWORD; do
    [ -n "${!v:-}" ] || { echo "build-apk: $v is not set (release builds are signed)." >&2; exit 1; }
  done
  [ -f "$CMMS_KEYSTORE" ] || { echo "build-apk: key file not found: $CMMS_KEYSTORE" >&2; exit 1; }
  args+=(-v "$CMMS_KEYSTORE":/signing/release.jks:ro -e CMMS_KEYSTORE_PASSWORD -e CMMS_KEY_ALIAS -e CMMS_KEY_PASSWORD)
  OUT_NAME="CMX-$VERSION.apk"
else
  OUT_NAME="CMX-$VERSION-debug.apk"
fi

docker run "${args[@]}" -e MODE="$MODE" -e OUT_NAME="$OUT_NAME" "$IMAGE" bash -euo pipefail -c '
  # Work on a copy: the repository stays read-only and the signing settings never reach the working tree.
  mkdir -p /work
  tar -C /repo --exclude=mobile/node_modules --exclude=mobile/out --exclude=mobile/android/.gradle \
      --exclude=mobile/android/app/build --exclude=mobile/android/build -cf - mobile VERSION | tar -C /work -xf -
  cd /work/mobile
  npm ci --no-audit --no-fund >/dev/null
  node test-config.mjs >/dev/null
  npx cap sync android >/dev/null
  cd android
  if [ "$MODE" = release ]; then
    printf "storeFile=/signing/release.jks\nstorePassword=%s\nkeyAlias=%s\nkeyPassword=%s\n" \
      "$CMMS_KEYSTORE_PASSWORD" "$CMMS_KEY_ALIAS" "$CMMS_KEY_PASSWORD" > keystore.properties
  fi
  variant=Debug; [ "$MODE" = release ] && variant=Release
  # The app'"'"'s unit tests (app/src/test) run before every build: no APK is made while one fails.
  ./gradlew --no-daemon --no-watch-fs -q "test${variant}UnitTest" "assemble${variant}"
  echo "Unit tests passed: $(cat app/build/test-results/test${variant}UnitTest/*.xml | grep -c "<testcase ")"
  cp "app/build/outputs/apk/$MODE/app-$MODE.apk" "/out/$OUT_NAME"
'
echo "Built mobile/out/$OUT_NAME"
