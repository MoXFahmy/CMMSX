#!/usr/bin/env bash
# Tests for scripts/release-lib.sh. Run: bash scripts/test-release.sh
set -u
cd "$(dirname "$0")" || exit 1
# shellcheck source=scripts/release-lib.sh
source ./release-lib.sh

failures=0
check() { # check <description> <expected exit: 0|1> <command...>
  local desc=$1 want=$2; shift 2
  "$@" >/dev/null 2>&1; local got=$?
  [ "$got" -ne 0 ] && got=1
  if [ "$got" -eq "$want" ]; then echo "ok   $desc"; else echo "FAIL $desc (exit $got, wanted $want)"; failures=$((failures + 1)); fi
}
equals() { # equals <description> <expected> <actual>
  if [ "$2" = "$3" ]; then echo "ok   $1"; else printf 'FAIL %s\n  expected: %q\n  actual:   %q\n' "$1" "$2" "$3"; failures=$((failures + 1)); fi
}

check "0.0.0 is a version"        0 is_semver 0.0.0
check "1.10.3 is a version"       0 is_semver 1.10.3
check "01.0.0 is not (leading 0)" 1 is_semver 01.0.0
check "1.0 is not"                1 is_semver 1.0
check "1.0.0-beta is not"         1 is_semver 1.0.0-beta
check "empty is not"              1 is_semver ""

check "0.0.1 > 0.0.0"             0 version_gt 0.0.1 0.0.0
check "0.1.0 > 0.0.9"             0 version_gt 0.1.0 0.0.9
check "0.10.0 > 0.9.0"            0 version_gt 0.10.0 0.9.0
check "1.0.0 > 0.99.99"           0 version_gt 1.0.0 0.99.99
check "0.0.0 is not > 0.0.0"      1 version_gt 0.0.0 0.0.0
check "0.0.1 is not > 0.1.0"      1 version_gt 0.0.1 0.1.0

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
cat > "$tmp/CHANGELOG.md" <<'EOF'
# Changelog

## [0.1.0] - 2026-12-01

### Added
- Offline work orders.

## [0.0.0] - 2026-11-19

### Added
- Everything so far.
EOF
check "changelog has 0.1.0"       0 changelog_has "$tmp/CHANGELOG.md" 0.1.0
check "changelog has 0.0.0"       0 changelog_has "$tmp/CHANGELOG.md" 0.0.0
check "changelog lacks 0.0.1"     1 changelog_has "$tmp/CHANGELOG.md" 0.0.1
equals "section 0.1.0 body" "$(printf '### Added\n- Offline work orders.')" "$(changelog_section "$tmp/CHANGELOG.md" 0.1.0)"
equals "section 0.0.0 body" "$(printf '### Added\n- Everything so far.')" "$(changelog_section "$tmp/CHANGELOG.md" 0.0.0)"

mkdir -p "$tmp/drive"
equals "drive dir from RELEASE_DRIVE_DIR" "$tmp/drive" "$(RELEASE_DRIVE_DIR="$tmp/drive" find_drive_dir)"
check "missing RELEASE_DRIVE_DIR fails" 1 env RELEASE_DRIVE_DIR="$tmp/nope" bash -c 'source ./release-lib.sh; find_drive_dir'

equals "version code 0.0.0" "1" "$(version_code 0.0.0)"
equals "version code 1.2.3" "1002004" "$(version_code 1.2.3)"

if [ "$failures" -eq 0 ]; then echo "All release-lib tests passed."; else echo "$failures failing"; exit 1; fi
