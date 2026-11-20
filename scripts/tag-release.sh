#!/usr/bin/env bash
# Tag a deployed release on GitHub. Run by .github/workflows/deploy-aws.yml after a successful deploy of main.
#
#   scripts/tag-release.sh <commit>
#
# Tags <commit> as vX.Y.Z, X.Y.Z being the VERSION file in that commit, and pushes the tag to origin. GitHub makes
# the release tags (not scripts/release.sh) because GitHub Desktop does not push tags made on the command line.
# A deployed commit whose version is already tagged on an earlier commit is not a release: that is a warning, and the
# tag stays where it is. Tested by scripts/test-release.sh.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
# shellcheck source=scripts/release-lib.sh
source "$ROOT/scripts/release-lib.sh"

[ $# -eq 1 ] || { echo "usage: scripts/tag-release.sh <commit>" >&2; exit 1; }
commit=$(git rev-parse --verify "$1^{commit}")
version=$(git show "$commit:VERSION" | tr -d '[:space:]')
is_semver "$version" || { echo "::error::VERSION in ${commit:0:7} is '$version', not X.Y.Z." >&2; exit 1; }
tag="v$version"

# The commit the tag marks on GitHub: an annotated tag's "^{}" line, else the tag itself; empty when there is none.
tagged=$(git ls-remote --tags origin "refs/tags/$tag" | awk -v t="refs/tags/$tag" '
  $2 == t "^{}" { peeled = $1 }  $2 == t { direct = $1 }  END { print (peeled != "" ? peeled : direct) }')

if [ "$tagged" = "$commit" ]; then
  echo "$tag is already on ${commit:0:7}."
elif [ -n "$tagged" ]; then
  echo "::warning::This deploy is not a release: $tag is already on ${tagged:0:7} and VERSION was not raised." \
    "Ship changes as a new version with scripts/release.sh (docs/VERSIONING.md)."
else
  git tag -a "$tag" -m "CMX $version" "$commit"
  git push origin "refs/tags/$tag"
  echo "Tagged ${commit:0:7} as $tag."
fi
