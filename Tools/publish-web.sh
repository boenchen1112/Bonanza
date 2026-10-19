#!/usr/bin/env bash
# Publish a static WebGL build to GitHub Pages (branch gh-pages of origin).
#   Tools/publish-web.sh [buildDir=Builds/Pages]
# Build it first with the decompression fallback on (a static host cannot send Content-Encoding
# for .br files): in the Editor run BonanzaBuild.BuildWebGLPages, or with the Editor closed:
#   Tools/unity-batch.sh exec BonanzaBuild.BuildWebGLPages
# Each publish force-pushes ONE fresh commit to gh-pages, so the repo does not grow per build.
# Playable at https://<owner>.github.io/<repo>/ . Pushes with gh's credentials (the git
# credential helper on this machine points at a missing binary).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIR="${1:-$ROOT/Builds/Pages}"
[ -f "$DIR/index.html" ] && [ -d "$DIR/Build" ] || { echo "no WebGL build in $DIR" >&2; exit 1; }
GH="$(command -v gh)" || { echo "gh not found" >&2; exit 1; }
REMOTE="$(git -C "$ROOT" remote get-url origin)"
SHA="$(git -C "$ROOT" rev-parse --short HEAD)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
cp -R "$DIR"/. "$TMP"/
touch "$TMP/.nojekyll"
cd "$TMP"
git init -q -b gh-pages
git add -A
git -c user.name="$(git -C "$ROOT" config user.name)" -c user.email="$(git -C "$ROOT" config user.email)" \
    commit -q -m "Publish web build of $SHA"
git -c credential.helper= -c "credential.helper=!$GH auth git-credential" push -f "$REMOTE" gh-pages
echo "published $SHA -> https://$(echo "$REMOTE" | sed -E 's#.*github.com[:/]([^/]+)/([^/.]+)(\.git)?#\1.github.io/\2#')/"
