#!/usr/bin/env bash
# Tests for scripts/release-lib.sh and scripts/tag-release.sh. Run: bash scripts/test-release.sh
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
contains() { # contains <description> <text> <output>
  case $3 in *"$2"*) echo "ok   $1" ;; *) printf 'FAIL %s\n  wanted to find: %s\n  in: %s\n' "$1" "$2" "$3"; failures=$((failures + 1)) ;; esac
}
not_contains() { # not_contains <description> <text> <output>
  case $3 in *"$2"*) printf 'FAIL %s\n  found: %s\n  in: %s\n' "$1" "$2" "$3"; failures=$((failures + 1)) ;; *) echo "ok   $1" ;; esac
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

# The APK must be signed with the release certificate (a lost or swapped key would make phones refuse the update).
signed_ok='Signer #1 certificate DN: CN=4C Systems
Signer #1 certificate SHA-256 digest: 8a39a5a907f74fe56ec8f1e9fd3bbc2bc39f4fe0b705b30e8455ecafd019e4b1
Signer #1 certificate SHA-1 digest: 0000'
signed_other='Signer #1 certificate SHA-256 digest: 1111111111111111111111111111111111111111111111111111111111111111'
check "the release certificate is accepted"     0 cert_matches "$signed_ok" 8a39a5a907f74fe56ec8f1e9fd3bbc2bc39f4fe0b705b30e8455ecafd019e4b1
check "another certificate is refused"          1 cert_matches "$signed_other" 8a39a5a907f74fe56ec8f1e9fd3bbc2bc39f4fe0b705b30e8455ecafd019e4b1
check "an unsigned APK is refused"              1 cert_matches "DOES NOT VERIFY" 8a39a5a907f74fe56ec8f1e9fd3bbc2bc39f4fe0b705b30e8455ecafd019e4b1

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

# ---- scripts/tag-release.sh: after a deploy, CI tags the commit; here a throwaway bare repo stands in for GitHub ----
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid
tag_script="$PWD/tag-release.sh"
work="$tmp/work"
git init -q --bare "$tmp/origin.git"
git init -q -b main "$work"
git -C "$work" remote add origin "$tmp/origin.git"
commit_version() { # commit_version <version>: commits VERSION=<version> and prints the commit
  printf '%s\n' "$1" > "$work/VERSION"
  git -C "$work" add VERSION && git -C "$work" commit -q --allow-empty -m "VERSION $1" && git -C "$work" rev-parse HEAD
}
tag_release() { (cd "$work" && bash "$tag_script" "$@"); }
github_tag() { git -C "$work" ls-remote origin "refs/tags/$1" | cut -f1; }           # the tag object on "GitHub"
github_tag_commit() { git -C "$work" ls-remote origin "refs/tags/$1^{}" | cut -f1; } # the commit it marks

c1=$(commit_version 0.0.1)
check "a deployed new version is tagged" 0 tag_release "$c1"
equals "v0.0.1 marks the deployed commit on GitHub" "$c1" "$(github_tag_commit v0.0.1)"
first_tag=$(github_tag v0.0.1)
equals "v0.0.1 is an annotated tag" "tag" "$(git -C "$work" cat-file -t "$first_tag" 2>/dev/null)"
check "deploying the same commit again succeeds" 0 tag_release "$c1"
again=$(tag_release "$c1" 2>&1)
contains "deploying it again says the tag is on the commit" "is already on ${c1:0:7}" "$again"
not_contains "deploying it again does not warn" "::warning::" "$again"
equals "deploying it again leaves the tag alone" "$first_tag" "$(github_tag v0.0.1)"

c2=$(commit_version 0.0.1)
check "a later commit with an unchanged VERSION still deploys" 0 tag_release "$c2"
equals "it does not move v0.0.1" "$c1" "$(github_tag_commit v0.0.1)"
check "it warns that the deploy is not a release" 0 bash -c "cd '$work' && bash '$tag_script' '$c2' | grep -q '::warning::.*not a release'"
later=$(tag_release "$c2" 2>&1)
contains "the warning names the commit the tag is on" "v0.0.1 is already on ${c1:0:7}" "$later"
not_contains "the warning does not name the tag object" "${first_tag:0:7}" "$later"

c3=$(commit_version 1.0)
check "a malformed VERSION fails" 1 tag_release "$c3"
equals "no tag is made for it" "" "$(git -C "$work" ls-remote --tags origin | grep -v 'v0\.0\.1')"
check "no commit given fails" 1 tag_release

if [ "$failures" -eq 0 ]; then echo "All release tests passed."; else echo "$failures failing"; exit 1; fi
