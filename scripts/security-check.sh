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
set -euo pipefail

GITLEAKS_IMAGE="zricethezav/gitleaks:v8.21.2"
DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
NODE_IMAGE="node:22"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
allowlist=".github/security-allowlist.txt"
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
  run_dotnet restore server/FourC.Cmms.slnx --verbosity quiet >"$work/restore.log" 2>&1 \
    || { cat "$work/restore.log"; echo "ERROR  dotnet restore failed"; problems=$((problems + 1)); return; }
  run_dotnet list server/FourC.Cmms.slnx package --vulnerable --include-transitive --format json >"$work/nuget.json" 2>"$work/nuget.err" \
    || { cat "$work/nuget.err"; echo "ERROR  dotnet list package failed"; problems=$((problems + 1)); return; }
  if jq -e '(.problems // []) | length > 0' "$work/nuget.json" >/dev/null; then
    jq -r '.problems[] | "ERROR  dotnet: \(.text // .)"' "$work/nuget.json"; problems=$((problems + 1)); return
  fi
  local severity package version project url ghsa
  # one line per advisory: severity, package, version, project, advisory url (a package in several projects is listed once each)
  while IFS=$'\t' read -r severity package version project url; do
    ghsa="${url##*/}"
    decide "$severity" yes "nuget $package $version ($project)" "$url" "$ghsa" "nuget:$package"
  done < <(jq -r '
      [.projects[]? | .path as $p | .frameworks[]? | (.topLevelPackages // [])[], (.transitivePackages // [])[]
        | . as $pk | (.vulnerabilities // [])[] | select(.severity == "High" or .severity == "Critical")
        | [.severity, $pk.id, ($pk.resolvedVersion // $pk.requestedVersion), ($p | split("/") | last), .advisoryurl]]
      | unique[] | @tsv' "$work/nuget.json")
}

# --- 2. npm ------------------------------------------------------------------------------------------------------------
check_npm() { # check_npm <dir>
  local dir=$1
  echo "== npm: npm audit --omit=dev ($dir)"
  # npm audit exits 1 when it finds anything; the JSON is what matters
  run_npm "$dir" audit --omit=dev --audit-level=high --json >"$work/npm-$dir.json" 2>"$work/npm-$dir.err" || true
  if ! jq -e '.vulnerabilities and (.error | not)' "$work/npm-$dir.json" >/dev/null 2>&1; then
    cat "$work/npm-$dir.err" 2>/dev/null; head -c 600 "$work/npm-$dir.json"; echo
    echo "ERROR  npm audit in $dir gave no usable result"; problems=$((problems + 1)); return
  fi
  local severity fixable name ids
  # one line per high/critical package: severity, fix available, package, advisory ids (GHSA ids found through the chain of "via")
  while IFS=$'\t' read -r severity fixable name ids; do
    # shellcheck disable=SC2086 # ids is a space-separated list on purpose
    decide "$severity" "$fixable" "npm $dir/$name" "$(jq -r --arg n "$name" '.vulnerabilities[$n] | "range \(.range)" + (if .fixAvailable|type=="object" then ", fix: \(.fixAvailable.name)@\(.fixAvailable.version)" else "" end)' "$work/npm-$dir.json")" $ids "npm:$name"
  done < <(jq -r '
      .vulnerabilities as $v
      | def ghsas($n; $depth):
          if $depth > 8 or ($v[$n] == null) then []
          else $v[$n].via | map(if type == "object" then [(.url // "" | split("/") | last)] else ghsas(.; $depth + 1) end) | add // [] end;
        $v | to_entries[] | select(.value.severity == "high" or .value.severity == "critical")
        | [.value.severity, (if (.value.fixAvailable // false) == false then "no" else "yes" end), .key,
           (ghsas(.key; 0) | map(select(length > 0)) | unique | join(" "))] | @tsv' "$work/npm-$dir.json")
}

# --- 3. secrets --------------------------------------------------------------------------------------------------------
gitleaks() { # gitleaks <report name> <extra args...>; findings -> $work/<report name>.json
  local report=$1; shift
  # run as the caller so git accepts the checkout's owner; .gitleaks.toml (if present) is picked up from the source root
  docker run --rm --user "$(id -u):$(id -g)" -v "$root":/src:ro -v "$work":/out -e HOME=/tmp "$GITLEAKS_IMAGE" \
    detect --source /src --config /src/.gitleaks.toml --redact --no-banner --exit-code 0 \
    --report-format json --report-path "/out/$report.json" "$@" >"$work/$report.log" 2>&1 \
    || { cat "$work/$report.log"; echo "ERROR  gitleaks failed"; problems=$((problems + 1)); return 1; }
}
report_secrets() { # report_secrets <report name> <where>
  local rule fingerprint file line
  [ -s "$work/$1.json" ] || return 0
  while IFS=$'\t' read -r rule fingerprint file line; do
    decide secret yes "$2 $file:$line" "$rule" "$fingerprint"
  done < <(jq -r '.[]? | [.RuleID, (.Fingerprint | sub("^/src/"; "")), (.File | sub("^/src/"; "")), .StartLine] | @tsv' "$work/$1.json")
}
check_secrets() {
  echo "== secrets: gitleaks ($GITLEAKS_IMAGE) over the working tree"
  gitleaks tree --no-git && report_secrets tree "tree"
  local range="${GITLEAKS_RANGE:-}"
  case "$range" in
    ''|0000000*) echo "== secrets: git history skipped (no GITLEAKS_RANGE, or a brand-new branch)"; return ;;
  esac
  # a force-push can leave a "before" commit that no longer exists; there is nothing to scan from there
  if ! git rev-parse --quiet --verify "${range%%..*}^{commit}" >/dev/null; then
    echo "== secrets: git history skipped (${range%%..*} is not in this clone)"; return
  fi
  echo "== secrets: gitleaks over the git history of $range"
  gitleaks history --log-opts="$range" && report_secrets history "history"
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
