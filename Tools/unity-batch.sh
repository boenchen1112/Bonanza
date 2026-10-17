#!/usr/bin/env bash
# Drive Unity in batch mode (no Editor UI, no unity CLI, no Hub). One Unity process at a time.
#   Tools/unity-batch.sh tests [EditMode|PlayMode]
#   Tools/unity-batch.sh build-webgl
#   Tools/unity-batch.sh exec <Namespace.Class.Method> [args...]   (UNITY_BATCH_SCRIPT env is passed through)
#   Tools/unity-batch.sh import
#   Tools/unity-batch.sh screenshot <scene> <out.png> [w h]
#   Tools/unity-batch.sh play-capture <scene> <outDir> [seconds] [every] [w h]
# Env: UNITY (editor binary), UNITY_TIMEOUT (seconds, overrides the per-command default).
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "$TOOLS_DIR/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.6.2f1/Unity.app/Contents/MacOS/Unity}"
LOGS="$PROJECT/Logs"
mkdir -p "$LOGS"

die() { echo "unity-batch: $*" >&2; exit 1; }
[ -x "$UNITY" ] || die "Unity binary not found: $UNITY (set UNITY=...)"

refuse_if_open() {
  local pids
  pids="$(pgrep -f 'Unity\.app/Contents/MacOS/Unity .*-projectPath' || true)"
  if [ -n "$pids" ]; then
    # only those whose command line names this project
    for p in $pids; do
      if ps -o command= -p "$p" 2>/dev/null | grep -qF "$PROJECT"; then
        die "another Unity process ($p) already has this project open"
      fi
    done
  fi
  # The Editor launched without -projectPath (Hub, double-click) shows the project in the lockfile holder.
  if [ -e "$PROJECT/Temp/UnityLockfile" ] && command -v lsof >/dev/null 2>&1; then
    if lsof "$PROJECT/Temp/UnityLockfile" >/dev/null 2>&1; then
      die "Temp/UnityLockfile is held by a running Unity process"
    fi
  fi
}

kill_tree() { # kill_tree <pid> <sig>
  local pid=$1 sig=${2:-TERM} c
  for c in $(pgrep -P "$pid" 2>/dev/null || true); do kill_tree "$c" "$sig"; done
  kill "-$sig" "$pid" 2>/dev/null || true
}

# run_unity <timeout-seconds> <logfile> <unity args...>; sets UNITY_RC
run_unity() {
  local limit=$1 log=$2; shift 2
  [ -n "${UNITY_TIMEOUT:-}" ] && limit=$UNITY_TIMEOUT
  refuse_if_open
  : > "$log"
  echo "unity-batch: $(basename "$UNITY") $* (timeout ${limit}s, log ${log#$PROJECT/})"
  "$UNITY" -batchmode -projectPath "$PROJECT" -logFile "$log" "$@" </dev/null >/dev/null 2>&1 &
  local upid=$!
  (
    sleep "$limit"
    echo "unity-batch: TIMEOUT after ${limit}s, killing process tree $upid" >&2
    touch "$LOGS/.batch-timeout"
    kill_tree "$upid" TERM; sleep 10; kill_tree "$upid" KILL
  ) &
  local wpid=$!
  rm -f "$LOGS/.batch-timeout"
  trap 'kill_tree "$upid" TERM; kill_tree "$wpid" KILL; exit 130' INT TERM
  set +e; wait "$upid"; UNITY_RC=$?; set -e
  trap - INT TERM
  kill_tree "$wpid" KILL; wait "$wpid" 2>/dev/null || true   # kill the watchdog's sleep too, or it holds our stdout open
  if [ -e "$LOGS/.batch-timeout" ]; then rm -f "$LOGS/.batch-timeout"; UNITY_RC=124; fi
  return 0
}

count_log() { # count_log <log> -> prints "errors=N warnings=M"
  local log=$1 e w
  e=$(grep -cE '(error CS[0-9]+|^\[BonanzaBuild\].*(failed|Exception)|^Error building Player|^[A-Za-z.]*Exception:|Build Failed)' "$log" || true)
  w=$(grep -cE 'warning CS[0-9]+|^WARNING:|^Shader warning' "$log" || true)
  echo "errors=$e warnings=$w"
}

report_tail() { echo "--- last log lines ---"; tail -n 25 "$1" || true; }

cmd_tests() {
  local mode=${1:-EditMode} xml="$LOGS/test-results.xml" log="$LOGS/batch-tests.log"
  case "$mode" in EditMode|PlayMode) ;; *) die "mode must be EditMode or PlayMode";; esac
  rm -f "$xml"
  run_unity "${TEST_TIMEOUT:-600}" "$log" -nographics -runTests -testPlatform "$mode" -testResults "$xml"
  [ "$UNITY_RC" = 124 ] && { report_tail "$log"; die "tests timed out"; }
  if [ ! -s "$xml" ]; then
    report_tail "$log"; die "no test results written (unity exit $UNITY_RC): compile error?"
  fi
  python3 -I - "$xml" <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
cases = list(r.iter("test-case"))
total = len(cases)
bad = [c for c in cases if c.get("result") not in ("Passed", "Skipped", "Ignored", "Inconclusive")]
skipped = [c for c in cases if c.get("result") in ("Skipped", "Ignored", "Inconclusive")]
passed = sum(1 for c in cases if c.get("result") == "Passed")
print(f"{passed}/{total} passed" + (f" ({len(skipped)} skipped)" if skipped else ""))
for c in bad:
    print("FAIL", c.get("fullname"))
    m = c.find("failure/message")
    if m is not None and m.text: print("   ", m.text.strip().splitlines()[0])
sys.exit(1 if bad or total == 0 else 0)
PY
}

# Unity leaves byte-identical outputs untouched on an incremental build, so "fresh" means:
# rewritten by this build, or newer than every runtime source (Editor/ and Tests/ code excluded).
newer_sources() {
  find "$PROJECT/Assets" "$PROJECT/Packages" "$PROJECT/ProjectSettings" -type f \
    \( -name '*.cs' -o -name '*.jslib' -o -name '*.unity' -o -name '*.shader' -o -name '*.hlsl' -o -name '*.asset' -o -name '*.prefab' -o -name '*.mat' -o -name '*.asmdef' \) \
    -not -path '*/Editor/*' -not -path '*/Tests/*' -newer "$1" 2>/dev/null
}

cmd_build_webgl() {
  local log="$LOGS/batch-build.log" out="$PROJECT/Builds/WebGL" mark
  mark="$LOGS/.build-start"; touch "$mark"; sleep 1
  run_unity "${BUILD_TIMEOUT:-1800}" "$log" -nographics -quit -executeMethod BonanzaBuild.BuildWebGL -outputPath "$out"
  echo "unity exit=$UNITY_RC; $(count_log "$log")"
  grep -E '^\[BonanzaBuild\] result=' "$log" || true
  if [ "$UNITY_RC" != 0 ]; then
    grep -E 'error CS|Exception|error:|Error building' "$log" | head -n 15 || true
    report_tail "$log"; die "build failed (exit $UNITY_RC)"
  fi
  local f missing=0
  for f in Build/WebGL.wasm.br Build/WebGL.data.br Build/WebGL.framework.js.br Build/WebGL.loader.js; do
    if [ ! -s "$out/$f" ]; then echo "MISSING $f"; missing=1
    elif [ ! "$out/$f" -nt "$mark" ] && [ -n "$(newer_sources "$out/$f")" ]; then
      echo "STALE   $f (older than $(newer_sources "$out/$f" | head -n1))"; missing=1
    elif [ ! "$out/$f" -nt "$mark" ]; then echo "ok      $f (unchanged by incremental build; newer than all runtime sources)"
    else echo "ok      $f ($(stat -f %z "$out/$f") bytes)"; fi
  done
  [ -s "$out/index.html" ] || { echo "MISSING index.html"; missing=1; }
  rm -f "$mark"
  [ "$missing" = 0 ] || die "build output incomplete or stale"
  echo "build ok -> Builds/WebGL (settings drift check: git diff --stat ProjectSettings)"
  git -C "$PROJECT" diff --stat -- ProjectSettings Packages || true
}

cmd_exec() {
  local method=${1:-}; [ -n "$method" ] || die "usage: exec <Namespace.Class.Method> [args...]"; shift
  local log="$LOGS/batch-exec.log"
  run_unity "${EXEC_TIMEOUT:-900}" "$log" -nographics -quit -executeMethod "$method" "$@"
  echo "unity exit=$UNITY_RC; $(count_log "$log")"
  [ "$UNITY_RC" = 0 ] || { report_tail "$log"; die "exec failed (exit $UNITY_RC)"; }
}

cmd_import() {
  local log="$LOGS/batch-import.log"
  run_unity "${IMPORT_TIMEOUT:-1200}" "$log" -nographics -quit -executeMethod BonanzaBuild.Import
  echo "unity exit=$UNITY_RC; $(count_log "$log")"
  [ "$UNITY_RC" = 0 ] || { report_tail "$log"; die "import failed (exit $UNITY_RC)"; }
}

abspath() { case "$1" in /*) echo "$1";; *) echo "$PWD/$1";; esac; }

cmd_screenshot() {
  [ $# -ge 2 ] || die "usage: screenshot <scene> <out.png> [w h]"
  local scene=$1 out; out="$(abspath "$2")"; local w=${3:-1280} h=${4:-720} log="$LOGS/batch-screenshot.log"
  rm -f "$out"; mkdir -p "$(dirname "$out")"
  # graphics required: no -nographics
  run_unity "${SHOT_TIMEOUT:-600}" "$log" -quit -executeMethod BonanzaBuild.Screenshot -scene "$scene" -out "$out" -width "$w" -height "$h"
  [ "$UNITY_RC" = 0 ] && [ -s "$out" ] || { report_tail "$log"; die "screenshot failed (exit $UNITY_RC)"; }
  echo "wrote $out"
}

cmd_play_capture() {
  [ $# -ge 2 ] || die "usage: play-capture <scene> <outDir> [seconds] [every] [w h]"
  local scene=$1 dir; dir="$(abspath "$2")"; local secs=${3:-10} every=${4:-2} w=${5:-1280} h=${6:-720} log="$LOGS/batch-play.log"
  mkdir -p "$dir"; rm -f "$dir"/frame-*.png
  # BONANZA_AUTOPLAY / BONANZA_GAME / BONANZA_PARTY pass through the environment.
  run_unity "${PLAY_TIMEOUT:-$((secs + 600))}" "$log" -executeMethod BonanzaBuild.PlayCapture -scene "$scene" -outDir "$dir" -seconds "$secs" -every "$every" -width "$w" -height "$h"
  local n; n=$(ls "$dir"/frame-*.png 2>/dev/null | wc -l | tr -d ' ')
  echo "unity exit=$UNITY_RC; frames=$n in $dir"
  [ "$UNITY_RC" = 0 ] && [ "$n" -gt 0 ] || { report_tail "$log"; die "play-capture failed"; }
}

sub=${1:-}; [ $# -gt 0 ] && shift
case "$sub" in
  tests) cmd_tests "$@";;
  build-webgl) cmd_build_webgl;;
  exec) cmd_exec "$@";;
  import) cmd_import;;
  screenshot) cmd_screenshot "$@";;
  play-capture) cmd_play_capture "$@";;
  *) sed -n '2,10p' "${BASH_SOURCE[0]}"; exit 2;;
esac
