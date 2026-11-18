#!/usr/bin/env bash
# Release 4C CMMS: one version number for the cloud app and the Android app. See docs/VERSIONING.md.
#
#   scripts/release.sh 0.1.0            new version: writes VERSION, builds the signed APK, commits, tags v0.1.0
#   scripts/release.sh 0.0.0 --no-bump  release the version already in VERSION (first release)
#
# Before running: add a "## [X.Y.Z] - YYYY-MM-DD" section to CHANGELOG.md and commit your work.
# Afterwards: push main and the tag from GitHub Desktop; the cloud app deploys itself, and the APK and release notes
# are in the Drive folder under Releases/vX.Y.Z/.
#
# Settings (environment): RELEASE_DRIVE_DIR (Drive folder, found automatically otherwise),
# RELEASE_SIGNING_DIR (default: <Drive folder>/_private/android-signing, holding signing.env and the key file).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
# shellcheck source=scripts/release-lib.sh
source scripts/release-lib.sh

die() { echo "release: $*" >&2; exit 1; }

NEW=${1:-}
MODE=${2:-}
is_semver "$NEW" || die "usage: scripts/release.sh X.Y.Z [--no-bump]"
CURRENT=$(tr -d '[:space:]' < VERSION)

[ -z "$(git status --porcelain)" ] || die "the working tree has uncommitted changes; commit or stash them first."
if [ "$MODE" = "--no-bump" ]; then
  [ "$NEW" = "$CURRENT" ] || die "--no-bump releases the current version ($CURRENT), not $NEW."
else
  [ -z "$MODE" ] || die "unknown option $MODE"
  version_gt "$NEW" "$CURRENT" || die "$NEW must be higher than the current version $CURRENT."
fi
if git rev-parse -q --verify "refs/tags/v$NEW" >/dev/null; then die "tag v$NEW already exists."; fi
changelog_has CHANGELOG.md "$NEW" || die "add a '## [$NEW] - $(date +%Y-%m-%d)' section to CHANGELOG.md first."

DRIVE=$(find_drive_dir) || die "Drive folder not found; set RELEASE_DRIVE_DIR to the '1000 CMX' folder."
SIGNING_DIR=${RELEASE_SIGNING_DIR:-$DRIVE/_private/android-signing}
[ -f "$SIGNING_DIR/signing.env" ] || die "no signing.env in $SIGNING_DIR (see docs/VERSIONING.md)."

# Signing settings; a relative key path is relative to the signing folder.
set -a
# shellcheck source=/dev/null
source "$SIGNING_DIR/signing.env"
set +a
case "${CMMS_KEYSTORE:-}" in /*) ;; "") die "signing.env has no CMMS_KEYSTORE." ;; *) CMMS_KEYSTORE="$SIGNING_DIR/$CMMS_KEYSTORE" ;; esac
export CMMS_KEYSTORE
[ -f "$CMMS_KEYSTORE" ] || die "signing key not found: $CMMS_KEYSTORE"

# Build first, so a failed build leaves no commit or tag behind.
if [ "$MODE" != "--no-bump" ]; then
  printf '%s\n' "$NEW" > VERSION
  trap 'echo "release: failed; undo the version change with: git checkout VERSION" >&2' ERR
fi
mobile/build-apk.sh release
APK="mobile/out/4C-CMMS-$NEW.apk"
[ -f "$APK" ] || die "the build did not produce $APK"
BUILT_CODE=$(docker run --rm -v "$ROOT/mobile/out":/out:ro cmms-android-build sh -c "aapt2 dump badging /out/4C-CMMS-$NEW.apk" \
  | sed -n "s/.*versionCode='\([0-9]*\)'.*/\1/p" | head -1)
[ "$BUILT_CODE" = "$(version_code "$NEW")" ] || die "APK versionCode $BUILT_CODE, expected $(version_code "$NEW")."
trap - ERR

if [ "$MODE" != "--no-bump" ]; then
  git add VERSION
  git commit -q -m "Release v$NEW"
fi
git tag -a "v$NEW" -m "4C CMMS $NEW"

OUT="$DRIVE/Releases/v$NEW"
mkdir -p "$OUT"
cp "$APK" "$OUT/"
{
  echo "# 4C CMMS $NEW"
  echo
  echo "Released $(date +%Y-%m-%d) · commit $(git rev-parse --short HEAD) · Android versionCode $BUILT_CODE"
  echo
  changelog_section CHANGELOG.md "$NEW"
  echo
  echo "## Install the Android app"
  echo
  echo "1. Open \`4C-CMMS-$NEW.apk\` on the phone (from Google Drive or a shared link)."
  echo "2. If asked, allow *Install unknown apps* for Drive or Files, then tap **Install**."
  echo "   Google Play Protect may warn about an app from outside the Play Store; choose to install anyway."
  echo "3. Open **4C CMMS** and sign in with your usual account. A newer version installs over this one and keeps you signed in."
} > "$OUT/RELEASE-NOTES.md"

echo
echo "Released 4C CMMS $NEW"
echo "  APK and notes: Drive > 01 CMMS > 1000 CMX > Releases > v$NEW"
echo "  Next: push main and the tag v$NEW from GitHub Desktop; the cloud app deploys itself."
