#!/usr/bin/env bash
# Tests for scripts/security-check.sh, with canned tool output (no Docker, no network). Run: bash scripts/test-security-check.sh
set -u
cd "$(dirname "$0")/.." || exit 1
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
failures=0

OK_NUGET='{"projects":[{"path":"/x/A.csproj"}]}'
OK_NPM='{"vulnerabilities":{}}'
# one package, two advisories (high with a fix, critical with a fix)
TWO_ADV='{"vulnerabilities":{"foo":{"name":"foo","severity":"critical","range":"<2","fixAvailable":true,"via":[
 {"title":"A","url":"https://github.com/advisories/GHSA-aaaa-aaaa-aaaa","severity":"high","range":"<1"},
 {"title":"B","url":"https://github.com/advisories/GHSA-bbbb-bbbb-bbbb","severity":"critical","range":"<2"}]}}}'
# jq cannot read this one: "via" is not a list
BAD_NPM='{"vulnerabilities":{"foo":{"name":"foo","severity":"high","range":"<2","fixAvailable":true,"via":7}}}'
BAD_SECRETS='{"not":"a list"}'

# run <name> <nuget> <web npm> <mobile npm> <tree secrets> [allowlist text]; leaves output in $tmp/out and the exit code in $code.
# The check refuses its test hooks under CI, so these runs clear CI and GITHUB_ACTIONS (this script itself runs in CI).
run() {
  local fx="$tmp/fx-$1"; mkdir -p "$fx"
  printf '%s' "$2" >"$fx/nuget.json"; printf '%s' "$3" >"$fx/npm-web.json"; printf '%s' "$4" >"$fx/npm-mobile.json"
  printf '%s' "$5" >"$fx/gitleaks-tree.json"
  printf '%s\n' "${6:-}" >"$tmp/allow-$1"
  env -u CI -u GITHUB_ACTIONS SECURITY_FIXTURES="$fx" SECURITY_ALLOWLIST="$tmp/allow-$1" GITLEAKS_RANGE='' \
    bash scripts/security-check.sh >"$tmp/out" 2>&1; code=$?
}
expect() { # expect <description> <exit code wanted> [text the output must contain]
  if [ "$code" -eq "$2" ] && { [ -z "${3:-}" ] || grep -qF -- "$3" "$tmp/out"; }; then echo "ok   $1"
  else echo "FAIL $1 (exit $code, wanted $2${3:+, output containing: $3})"; sed 's/^/     | /' "$tmp/out"; failures=$((failures + 1)); fi
}
future="$(date -u -d '+30 days' +%F 2>/dev/null || date -u -v+30d +%F)"

run clean "$OK_NUGET" "$OK_NPM" "$OK_NPM" ''
expect "clean output passes" 0 "clean"

# npm: allowlisting one advisory must not hide the other advisory of the same package
run adv0 "$OK_NUGET" "$TWO_ADV" "$OK_NPM" ''
expect "two advisories, none allowed: blocks, names both" 1 "GHSA-bbbb-bbbb-bbbb"
grep -qF GHSA-aaaa-aaaa-aaaa "$tmp/out" || { echo "FAIL first advisory not named"; failures=$((failures + 1)); }
run adv1 "$OK_NUGET" "$TWO_ADV" "$OK_NPM" '' "GHSA-aaaa-aaaa-aaaa|reviewed|$future"
expect "two advisories, one allowed: still blocks on the other" 1 "FOUND    [critical] npm web/foo"
run adv2 "$OK_NUGET" "$TWO_ADV" "$OK_NPM" '' "GHSA-aaaa-aaaa-aaaa|reviewed|$future
GHSA-bbbb-bbbb-bbbb|reviewed|$future"
expect "two advisories, both allowed: passes" 0 "clean"
run adv3 "$OK_NUGET" "$TWO_ADV" "$OK_NPM" '' "npm:foo|whole package reviewed|$future"
expect "package-level npm:foo entry: passes" 0 "clean"

# NuGet output that is not a real result
run n1 "" "$OK_NPM" "$OK_NPM" ''
expect "empty nuget.json blocks" 1 "no usable result"
run n2 "this is not json" "$OK_NPM" "$OK_NPM" ''
expect "non-JSON nuget.json blocks" 1 "no usable result"
run n3 '{"projects":[]}' "$OK_NPM" "$OK_NPM" ''
expect '{"projects":[]} blocks' 1 "no usable result"
run n4 '{"projects":[{"path":"/x/A.csproj","frameworks":[{"topLevelPackages":[{"id":"P","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"High","advisoryurl":"https://github.com/advisories/GHSA-cccc-cccc-cccc"}]}]}]}]}' "$OK_NPM" "$OK_NPM" ''
expect "a High NuGet advisory blocks" 1 "GHSA-cccc-cccc-cccc"

# jq failing while reading otherwise-valid-looking output
run j1 "$OK_NUGET" "$BAD_NPM" "$OK_NPM" ''
expect "jq failure reading npm output blocks" 1 "could not read the tool output"
run j2 "$OK_NUGET" "$OK_NPM" "$OK_NPM" "$BAD_SECRETS"
expect "jq failure reading the secrets report blocks" 1 "could not read the tool output"
run s1 "$OK_NUGET" "$OK_NPM" "$OK_NPM" '[{"RuleID":"aws-access-token","Fingerprint":"/src/a.txt:aws-access-token:1","File":"/src/a.txt","StartLine":1}]'
expect "a secret blocks" 1 "a.txt:aws-access-token:1"

# unfixable npm findings warn without blocking
run w1 "$OK_NUGET" '{"vulnerabilities":{"bar":{"name":"bar","severity":"high","range":"*","fixAvailable":false,"via":[{"title":"C","url":"https://github.com/advisories/GHSA-dddd-dddd-dddd","severity":"high"}]}}}' "$OK_NPM" ''
expect "unfixable high npm finding only warns" 0 "WARNING"

# the test hooks are refused in CI, whichever CI variable is set and whichever hook: a polluted environment must not pass the gate.
# Stand-ins for dotnet, npm and docker fail at once, so a check that wrongly ran the real tools would end fast (and still fail here).
fake="$tmp/fake-bin"; mkdir -p "$fake"
for tool in dotnet npm docker; do printf '#!/bin/sh\nexit 1\n' >"$fake/$tool"; chmod +x "$fake/$tool"; done
hook_run() { # hook_run <env assignments...>: the clean fixtures, under the given environment
  env -u CI -u GITHUB_ACTIONS PATH="$fake:$PATH" GITLEAKS_RANGE='' "$@" bash scripts/security-check.sh >"$tmp/out" 2>&1; code=$?
}
hook_run CI=true SECURITY_FIXTURES="$tmp/fx-clean" SECURITY_ALLOWLIST="$tmp/allow-clean"
expect "fixture and allowlist hooks refused when CI is set" 1 "test hook"
hook_run GITHUB_ACTIONS=true SECURITY_FIXTURES="$tmp/fx-clean"
expect "fixture hook refused when GITHUB_ACTIONS is set" 1 "test hook"
hook_run CI=1 SECURITY_ALLOWLIST="$tmp/allow-clean"
expect "allowlist hook alone refused when CI is set" 1 "test hook"

if [ "$failures" -gt 0 ]; then echo "$failures FAILED"; exit 1; fi
echo "all passed"
