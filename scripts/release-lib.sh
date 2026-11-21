#!/usr/bin/env bash
# Helpers for scripts/release.sh (sourced; no side effects). Tested by scripts/test-release.sh.

# is_semver <v>: X.Y.Z with non-negative integers and no leading zeros.
is_semver() {
  [[ "${1:-}" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]
}

# version_gt <a> <b>: true when a is a higher version than b.
version_gt() {
  local a1 a2 a3 b1 b2 b3
  IFS=. read -r a1 a2 a3 <<<"$1"
  IFS=. read -r b1 b2 b3 <<<"$2"
  ((a1 != b1)) && { ((a1 > b1)); return; }
  ((a2 != b2)) && { ((a2 > b2)); return; }
  ((a3 > b3))
}

# version_code <v>: Android versionCode = major*1,000,000 + minor*1,000 + patch + 1 (0.0.0 -> 1). Same formula as
# mobile/android/app/build.gradle; release.sh checks the built APK against it.
version_code() {
  local m n p
  IFS=. read -r m n p <<<"$1"
  echo $((m * 1000000 + n * 1000 + p + 1))
}

# changelog_has <file> <v>: the file has a "## [v]" heading.
changelog_has() {
  grep -qE "^## \[${2//./\\.}\]" "$1"
}

# changelog_section <file> <v>: the body of version v's section (without its heading or surrounding blank lines).
changelog_section() {
  awk -v v="$2" '
    index($0, "## [" v "]") == 1 { on = 1; next }
    on && /^## \[/ { exit }
    on { lines[++n] = $0 }
    END {
      first = 1; while (first <= n && lines[first] ~ /^[[:space:]]*$/) first++
      last = n;  while (last >= first && lines[last] ~ /^[[:space:]]*$/) last--
      for (i = first; i <= last; i++) print lines[i]
    }' "$1"
}

# find_drive_dir: the shared Drive folder for release outputs. $RELEASE_DRIVE_DIR when set, else the single
# "01 CMMS/1000 CMX" folder under Google Drive for desktop. The path is found, never written in scripts, because it
# contains the account name.
find_drive_dir() {
  if [ -n "${RELEASE_DRIVE_DIR:-}" ]; then
    [ -d "$RELEASE_DRIVE_DIR" ] && { echo "$RELEASE_DRIVE_DIR"; return 0; }
    return 1
  fi
  local matches=()
  for d in "$HOME"/Library/CloudStorage/GoogleDrive-*/"My Drive/01 CMMS/1000 CMX"; do
    [ -d "$d" ] && matches+=("$d")
  done
  [ "${#matches[@]}" -eq 1 ] || return 1
  echo "${matches[0]}"
}

# The release certificate's SHA-256 fingerprint (public; the key itself stays in Drive › _private). Phones only accept
# updates signed with this certificate.
# shellcheck disable=SC2034 # used by release.sh
RELEASE_CERT_SHA256=8a39a5a907f74fe56ec8f1e9fd3bbc2bc39f4fe0b705b30e8455ecafd019e4b1

# cert_matches <apksigner verify --print-certs output> <expected sha-256>: the first signer is the expected certificate.
cert_matches() {
  local got
  got=$(printf '%s\n' "$1" | sed -n 's/^Signer #1 certificate SHA-256 digest: *\([0-9a-f]*\).*/\1/p' | head -1)
  [ -n "$got" ] && [ "$got" = "$2" ]
}
