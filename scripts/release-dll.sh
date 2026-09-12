#!/usr/bin/env bash
#
# Build the plugin DLL and attach it to a GitHub release.
#
# The plugin references Liftoff's and BepInEx's copyrighted managed assemblies,
# which are not in this repo and cannot be shipped to GitHub-hosted CI. So the
# binary for a release is built here, on a machine that owns a copy of the game,
# and uploaded to the release the `release` workflow already created.
#
# Usage:
#   scripts/release-dll.sh              # build + upload for the current <Version>
#   scripts/release-dll.sh --no-upload  # build + package only, upload nothing
#
# Override the game location with LIFTOFF_DIR if it isn't in a standard Steam
# library (the .csproj probes the usual Linux and Windows paths).

set -euo pipefail

cd "$(dirname "$0")/.."

upload=1
[ "${1:-}" = "--no-upload" ] && upload=0

name=JmtLiftoffLeaderboard
project=src/$name
version=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
tag="v$version"
revision=$(git rev-parse --short HEAD)
outdir="dist/$tag"

echo "==> version $version  tag $tag  revision $revision"

if [ -n "$(git status --porcelain)" ]; then
  echo "!! working tree is dirty — the DLL would not match any commit" >&2
  git status --short >&2
  exit 1
fi

if ! git rev-parse "$tag" >/dev/null 2>&1; then
  echo "!! tag $tag does not exist — create and push it first (see docs/versioning.md)" >&2
  exit 1
fi

# The DLL carries its build marker. If HEAD isn't the tagged commit, that marker
# would point at a commit the release doesn't contain.
if [ "$(git rev-parse HEAD)" != "$(git rev-parse "$tag^{commit}")" ]; then
  echo "!! HEAD is not $tag — check out the tag before building a release binary" >&2
  exit 1
fi

echo "==> building"
rm -rf "$project/bin" "$project/obj"
dotnet build "$project/$name.csproj" -c Release -p:BuildRevision="$revision"

built="$project/bin/Release/net472/$name.dll"
[ -f "$built" ] || { echo "!! build produced no DLL" >&2; exit 1; }

echo "==> packaging"
rm -rf "$outdir" && mkdir -p "$outdir"
cp "$built" "$project/bin/Release/net472/$name.pdb" LICENSE "$outdir/"
cp docs/install.md "$outdir/INSTALL.md"
( cd "$outdir" && zip -q "$name-$version.zip" "$name.dll" "$name.pdb" LICENSE INSTALL.md )
( cd "$outdir" && sha256sum "$name.dll" "$name-$version.zip" > SHA256SUMS.txt )

echo
cat "$outdir/SHA256SUMS.txt"
echo

if [ "$upload" -eq 0 ]; then
  echo "==> --no-upload: artifacts left in $outdir"
  exit 0
fi

echo "==> uploading to release $tag"
gh release upload "$tag" \
  "$outdir/$name.dll" \
  "$outdir/$name-$version.zip" \
  "$outdir/SHA256SUMS.txt" \
  --clobber

echo "==> done: $(gh release view "$tag" --json url -q .url)"
