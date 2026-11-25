#!/usr/bin/env bash
# Supply-chain checks: known-vulnerable packages and leaked secrets.
#
#   bash scripts/security-check.sh
#
# Checks, in this order:
#   1. NuGet   dotnet list server/FourC.Cmms.slnx package --vulnerable --include-transitive
#   2. npm     npm audit --omit=dev, in web and in mobile
#   3. Secrets gitleaks over the working tree, and over the git history of a range when GITLEAKS_RANGE is set
#
# The script exits 1 when any of these is found and not allowed:
#   - a High or Critical NuGet advisory (dotnet cannot say whether a fix exists, so every one counts);
#   - a High or Critical npm advisory that has a fix available (ones without a fix are printed as warnings only);
#   - any secret.
# Every finding is printed with its id(s). A finding is allowed only by a line in .github/security-allowlist.txt:
#   <id>|<reason>|<expires YYYY-MM-DD>
# Ids: a GHSA-... advisory id, "nuget:<package>", "npm:<package>", or a gitleaks fingerprint. An entry works only while its
# expiry date (UTC) is in the future and at most 90 days away; a malformed or too-distant entry is itself an error.
# False positives in the secret scan (deliberate fake credentials in tests) go in .gitleaks.toml instead, not in this file.
#
# Tools: uses dotnet and npm when they are installed (CI) and Docker images otherwise (a laptop). gitleaks always runs from its
# pinned Docker image. Environment:
#   GITLEAKS_RANGE  git revision range to scan the history of, e.g. "$BEFORE..$AFTER" (unset or all-zero start: skipped)
#   SECURITY_TODAY  override today's date (YYYY-MM-DD), for testing the expiry rules
#   SECURITY_ALLOWLIST  test hook: another allowlist file instead of .github/security-allowlist.txt
#   SECURITY_FIXTURES  test hook (scripts/test-security-check.sh): a directory holding canned tool output instead of running the
#                   tools: nuget.json, npm-web.json, npm-mobile.json, gitleaks-tree.json.
#   Either test hook set while CI or GITHUB_ACTIONS is set: the script refuses to run (one problem, exit 1).
# Fail closed: output of a tool that is missing, empty, not JSON or not shaped as expected counts as a problem, never as clean.
set -euo pipefail

GITLEAKS_IMAGE="zricethezav/gitleaks:v8.21.2"
DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
NODE_IMAGE="node:22"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
allowlist="${SECURITY_ALLOWLIST:-.github/security-allowlist.txt}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

command -v jq >/dev/null || { echo "security-check: jq is required" >&2; exit 2; }

today="${SECURITY_TODAY:-$(date -u +%F)}"
# today + 90 days, with GNU date or BSD date
if max_expiry="$(date -u -d "$today +90 days" +%F 2>/dev/null)"; then :
else max_expiry="$(date -u -j -v+90d -f %Y-%m-%d "$today" +%F)"; fi

problems=0
warnings=0
allowed_file="$work/allowed"; : >"$allowed_file"   # lines: <id> TAB <expiry>, only entries still in force

# The test hooks replace the tools' output or the allowlist. In CI the gate must judge the real ones, so a CI run with either
# hook set (a polluted environment) is refused rather than trusted. scripts/test-security-check.sh clears CI for its own runs.
if { [ -n "${CI:-}" ] || [ -n "${GITHUB_ACTIONS:-}" ]; } && { [ -n "${SECURITY_FIXTURES:-}" ] || [ -n "${SECURITY_ALLOWLIST:-}" ]; }; then
  echo "ERROR  SECURITY_FIXTURES or SECURITY_ALLOWLIST is set in CI: these are test hooks for scripts/test-security-check.sh and are refused here"
  problems=$((problems + 1))
  echo "security-check: $problems problem(s) found; unset the test hooks."
  exit 1
fi

# --- allowlist ---------------------------------------------------------------------------------------------------------
load_allowlist() {
  [ -f "$allowlist" ] || return 0
  local line id reason expires n=0
  while IFS= read -r line || [ -n "$line" ]; do
    n=$((n + 1))
    case "$line" in ''|'#'*) continue ;; esac
    IFS='|' read -r id reason expires <<<"$line"
    if [ -z "${id:-}" ] || [ -z "${reason:-}" ] || ! [[ "${expires:-}" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
      echo "ERROR  $allowlist:$n is not '<id>|<reason>|<expires YYYY-MM-DD>': $line"; problems=$((problems + 1)); continue
    fi
    if [[ "$expires" > "$max_expiry" ]]; then
      echo "ERROR  $allowlist:$n ($id) expires $expires, more than 90 days away (latest allowed: $max_expiry)"; problems=$((problems + 1)); continue
    fi
    if [[ "$expires" > "$today" ]]; then printf '%s\t%s\n' "$id" "$expires" >>"$allowed_file"
    else echo "NOTE   $allowlist:$n ($id) expired on $expires, so it no longer allows anything"; fi
  done <"$allowlist"
}

# decide <severity> <fixable: yes|no> <source> <summary> <id>...   (counts the finding unless it is allowed)
decide() {
  local severity=$1 fixable=$2 source=$3 summary=$4; shift 4
  local id exp hit=""
  for id in "$@"; do
    exp=$(awk -F'\t' -v id="$id" '$1 == id { print $2; exit }' "$allowed_file")
    if [ -n "$exp" ]; then hit="$id (allowed until $exp)"; break; fi
  done
  if [ -n "$hit" ]; then
    echo "ALLOWED  [$severity] $source: $summary  ids: $* -> $hit"
  elif [ "$fixable" = no ]; then
    echo "WARNING  [$severity] $source: $summary  ids: $*  (no fix available yet; not blocking)"; warnings=$((warnings + 1))
  else
    echo "FOUND    [$severity] $source: $summary  ids: $*"; problems=$((problems + 1))
  fi
}

# tool_output <file> <command...>: run the tool into <file>, or copy the canned output when SECURITY_FIXTURES is set
tool_output() {
  local out=$1 name; shift
  if [ -n "${SECURITY_FIXTURES:-}" ]; then
    name="$(basename "$out")"
    cp "$SECURITY_FIXTURES/$name" "$out" 2>/dev/null || : >"$out"
  else "$@" >"$out" 2>"$out.err"; fi
}
# extract <outfile> <jq args...>: run jq into <outfile>; a jq failure is a problem (never a silent "no findings")
extract() {
  local out=$1; shift
  jq -r "$@" >"$out" 2>"$out.err" || { cat "$out.err"; echo "ERROR  could not read the tool output ($out)"; problems=$((problems + 1)); return 1; }
}

# --- tool runners ------------------------------------------------------------------------------------------------------
run_dotnet() {
  if command -v dotnet >/dev/null; then dotnet "$@"
  else docker run --rm -v "$root":/src -v cmx-nuget:/root/.nuget -w /src -e DOTNET_CLI_TELEMETRY_OPTOUT=1 "$DOTNET_IMAGE" dotnet "$@"; fi
}
run_npm() { # run_npm <dir> <npm args...>
  local dir=$1; shift
  if command -v npm >/dev/null; then (cd "$dir" && npm "$@")
  else docker run --rm -v "$root":/src -v cmx-npm:/root/.npm -w "/src/$dir" "$NODE_IMAGE" npm "$@"; fi
}

# --- 1. NuGet ----------------------------------------------------------------------------------------------------------
check_nuget() {
  echo "== NuGet: dotnet list package --vulnerable --include-transitive"
  if [ -z "${SECURITY_FIXTURES:-}" ]; then
    run_dotnet restore server/FourC.Cmms.slnx --verbosity quiet >"$work/restore.log" 2>&1 \
      || { cat "$work/restore.log"; echo "ERROR  dotnet restore failed"; problems=$((problems + 1)); return; }
  fi
  tool_output "$work/nuget.json" run_dotnet list server/FourC.Cmms.slnx package --vulnerable --include-transitive --format json \
    || { cat "$work/nuget.json.err"; echo "ERROR  dotnet list package failed"; problems=$((problems + 1)); return; }
  # a real result always lists the projects, so an empty or foreign document means the tool did not do its job
  if ! jq -e '.projects | type == "array" and length > 0' "$work/nuget.json" >/dev/null 2>&1; then
    head -c 600 "$work/nuget.json"; echo
    echo "ERROR  dotnet list package gave no usable result"; problems=$((problems + 1)); return
  fi
  if jq -e '(.problems // []) | length > 0' "$work/nuget.json" >/dev/null; then
    jq -r '.problems[] | "ERROR  dotnet: \(.text // .)"' "$work/nuget.json"; problems=$((problems + 1)); return
  fi
  local severity package version project url ghsa
  # one line per advisory: severity, package, version, project, advisory url (a package in several projects is listed once each)
  # shellcheck disable=SC2016 # jq program, not shell
  extract "$work/nuget.tsv" '
      [.projects[]? | .path as $p | .frameworks[]? | (.topLevelPackages // [])[], (.transitivePackages // [])[]
        | . as $pk | (.vulnerabilities // [])[] | select(.severity == "High" or .severity == "Critical")
        | [.severity, $pk.id, ($pk.resolvedVersion // $pk.requestedVersion), ($p | split("/") | last), .advisoryurl]]
      | unique[] | @tsv' "$work/nuget.json" || return 0
  while IFS=$'\t' read -r severity package version project url; do
    ghsa="${url##*/}"
    decide "$severity" yes "nuget $package $version ($project)" "$url" "$ghsa" "nuget:$package"
  done <"$work/nuget.tsv"
}

# --- 2. npm ------------------------------------------------------------------------------------------------------------
check_npm() { # check_npm <dir>
  local dir=$1
  local file="$work/npm-$dir.json"
  echo "== npm: npm audit --omit=dev ($dir)"
  # npm audit exits 1 when it finds anything; the JSON is what matters
  tool_output "$file" run_npm "$dir" audit --omit=dev --audit-level=high --json || true
  if ! jq -e '(.vulnerabilities | type == "object") and (.error | not)' "$file" >/dev/null 2>&1; then
    cat "$file.err" 2>/dev/null; head -c 600 "$file"; echo
    echo "ERROR  npm audit in $dir gave no usable result"; problems=$((problems + 1)); return
  fi
  local severity fixable name ghsa detail
  # One line per high/critical advisory found in a package's chain of "via" (so a package with two advisories gives two lines,
  # each judged on its own GHSA id); "-" stands for a package whose advisory detail is missing. "npm:<name>" allows them all.
  # shellcheck disable=SC2016 # jq program, not shell
  extract "$work/npm-$dir.tsv" '
      .vulnerabilities as $v
      | def adv($n; $depth):
          if $depth > 8 or ($v[$n] == null) then []
          else $v[$n].via | map(if type == "object" then [.] else adv(.; $depth + 1) end) | add // [] end;
        $v | to_entries[] | select(.value.severity == "high" or .value.severity == "critical")
        | . as $e
        | (adv($e.key; 0) | map(select(.severity == "high" or .severity == "critical")) | unique_by(.url // .title)) as $advs
        | (if ($advs | length) == 0 then [{severity: $e.value.severity, url: "-", title: "no advisory detail"}] else $advs end)[]
        | [.severity, (if ($e.value.fixAvailable // false) == false then "no" else "yes" end), $e.key,
           ((.url // "-") | split("/") | last | if . == "" then "-" else . end),
           ((.title // "") | gsub("[\t\n]"; " ")) + ", range " + ($e.value.range // "?")
             + (if ($e.value.fixAvailable | type) == "object" then ", fix: \($e.value.fixAvailable.name)@\($e.value.fixAvailable.version)" else "" end)]
        | @tsv' "$file" || return 0
  while IFS=$'\t' read -r severity fixable name ghsa detail; do
    if [ "$ghsa" = "-" ]; then decide "$severity" "$fixable" "npm $dir/$name" "$detail" "npm:$name"
    else decide "$severity" "$fixable" "npm $dir/$name" "$detail" "$ghsa" "npm:$name"; fi
  done <"$work/npm-$dir.tsv"
}

# --- 3. secrets --------------------------------------------------------------------------------------------------------
gitleaks() { # gitleaks <report name> <extra args...>; findings -> $work/<report name>.json
  local report=$1; shift
  if [ -n "${SECURITY_FIXTURES:-}" ]; then cp "$SECURITY_FIXTURES/gitleaks-$report.json" "$work/$report.json" 2>/dev/null || : >"$work/$report.json"; return 0; fi
  # run as the caller so git accepts the checkout's owner; .gitleaks.toml (if present) is picked up from the source root
  docker run --rm --user "$(id -u):$(id -g)" -v "$root":/src:ro -v "$work":/out -e HOME=/tmp "$GITLEAKS_IMAGE" \
    detect --source /src --config /src/.gitleaks.toml --redact --no-banner --exit-code 0 \
    --report-format json --report-path "/out/$report.json" "$@" >"$work/$report.log" 2>&1 \
    || { cat "$work/$report.log"; echo "ERROR  gitleaks failed"; problems=$((problems + 1)); return 1; }
}
report_secrets() { # report_secrets <report name> <where>
  local rule fingerprint file line
  # gitleaks writes no report when it finds nothing; a report that is not a list of findings is unreadable
  [ -s "$work/$1.json" ] || return 0
  # shellcheck disable=SC2016 # jq program, not shell
  extract "$work/$1.tsv" '.[] | [.RuleID, (.Fingerprint | sub("^/src/"; "")), (.File | sub("^/src/"; "")), .StartLine] | @tsv' "$work/$1.json" || return 0
  while IFS=$'\t' read -r rule fingerprint file line; do
    decide secret yes "$2 $file:$line" "$rule" "$fingerprint"
  done <"$work/$1.tsv"
}
check_secrets() {
  echo "== secrets: gitleaks ($GITLEAKS_IMAGE) over the working tree"
  if gitleaks tree --no-git; then report_secrets tree "tree"; fi
  local range="${GITLEAKS_RANGE:-}"
  case "$range" in
    ''|0000000*) echo "== secrets: git history skipped (no GITLEAKS_RANGE, or a brand-new branch)"; return ;;
  esac
  # a force-push can leave a "before" commit that no longer exists; there is nothing to scan from there
  if ! git rev-parse --quiet --verify "${range%%..*}^{commit}" >/dev/null; then
    echo "== secrets: git history skipped (${range%%..*} is not in this clone)"; return
  fi
  echo "== secrets: gitleaks over the git history of $range"
  if gitleaks history --log-opts="$range"; then report_secrets history "history"; fi
}

load_allowlist
check_nuget
check_npm web
check_npm mobile
check_secrets

echo
if [ "$problems" -gt 0 ]; then
  echo "security-check: $problems problem(s) found ($warnings warning(s)). Fix or update the package, or add a reasoned, dated entry to $allowlist."
  exit 1
fi
echo "security-check: clean ($warnings warning(s))."
