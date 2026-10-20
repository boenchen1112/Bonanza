#!/bin/bash
# Prepares the Hymn Meter data for a Unity build from the PRIVATE hymns folder (notes/hymns by default).
# Nothing it writes is committed: Assets/Resources/ and HymnBuild/ are gitignored.
#
#   Tools/hymns/prepare-data.sh            tempo maps (only missing ones), copy the data in, render the audio
#   Tools/hymns/prepare-data.sh --force    also re-render every clip
#   Tools/hymns/prepare-data.sh --clean    remove the data again (a build then has no hymns: the game
#                                          reports "hymn data missing" and the other games are unaffected)
#   BONANZA_HYMNS=<dir>                    another hymns folder (hymn_NNN.mid, tempo/, excerpts.txt)
#
# After editing a tempo map (notes/hymns/tempo/hymn_NNN.json) or excerpts.txt run it again: it prints which
# clips changed. Then rebuild the player (Tools/unity-batch.sh build-webgl).
set -euo pipefail
cd "$(dirname "$0")/../.."
HYMNS="${BONANZA_HYMNS:-$PWD/notes/hymns}"
OUT="$PWD/Assets/Resources/Hymns"

if [ "${1:-}" = "--clean" ]; then
  rm -rf "$OUT" "$PWD/HymnBuild" "$PWD/Assets/Resources/Hymns.meta"
  rmdir "$PWD/Assets/Resources" 2>/dev/null && rm -f "$PWD/Assets/Resources.meta" || true
  echo "hymn data removed"
  exit 0
fi

[ -f "$HYMNS/excerpts.txt" ] || { echo "no $HYMNS/excerpts.txt: the hymns folder is missing" >&2; exit 1; }
export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

# 1. a tempo map for every hymn that has none (existing maps are never touched)
node Tools/hymns/make-tempo-maps.mjs --hymns "$HYMNS" | grep -v '^kept' || true

# 2. the data the player loads: the MIDI as a .bytes asset, the tempo map, the excerpt table
mkdir -p "$OUT"
cp "$HYMNS/excerpts.txt" "$OUT/excerpts.txt"
used=$(awk '{sub(/#.*/,"")} NF==6 && $6=="yes" {print $1}' "$HYMNS/excerpts.txt")
for h in $used; do
  [ -f "$HYMNS/$h.mid" ] || { echo "excerpts.txt names $h but $HYMNS/$h.mid is missing" >&2; exit 1; }
  cp "$HYMNS/$h.mid" "$OUT/$h.mid.bytes"
  if [ -f "$HYMNS/tempo/$h.json" ]; then cp "$HYMNS/tempo/$h.json" "$OUT/$h.tempo.json"; else rm -f "$OUT/$h.tempo.json"; fi
done
for f in "$OUT"/hymn_*.mid.bytes; do   # hymns no longer in use
  [ -e "$f" ] || continue
  b=$(basename "$f" .mid.bytes)
  echo "$used" | grep -qx "$b" || { rm -f "$f" "$f.meta" "$OUT/$b.tempo.json" "$OUT/$b.tempo.json.meta"; echo "removed $b (use no)"; }
done

# 3. the audio and the beat maps
node Tools/hymns/render-hymns.mjs --hymns "$HYMNS" --out "$OUT" "$@"
echo "hymn data ready in $OUT"
