"""Static file server for the Phase B hand-tracking test page, plus
logging endpoints -- serves index.html, receives per-sample position logs
and a final snapshot PNG from the browser, and writes them to disk
automatically (feedback, 2026-07-29: live/interactive tools must persist a
record, not rely on the user describing what they saw).

COOP/COEP headers are set on every response because MediaPipe's WASM
runtime needs them (SharedArrayBuffer requires cross-origin isolation) --
opening index.html directly via file:// or a plain static server without
these headers will fail silently or with a console error about
SharedArrayBuffer, not a symptom that points back to the actual cause.

Usage:
    python server.py
Then open http://localhost:8090/ in a browser.
"""

import base64
import csv
import http.server
import json
import os
import threading
import time

# 8080 was requested originally, but is already occupied by an unrelated
# server on this machine (verified via netstat, 2026-07-29) -- 8090 is free.
PORT = 8090
DIR = os.path.dirname(os.path.abspath(__file__))
LOG_DIR = os.path.join(DIR, "logs")
os.makedirs(LOG_DIR, exist_ok=True)

_open_logs: dict[str, csv.writer] = {}
_open_files: dict[str, object] = {}
_open_score_logs: dict[str, csv.writer] = {}
_open_score_files: dict[str, object] = {}
# Audit D1: the server is now ThreadingHTTPServer (one request no longer
# blocks the next), so the writer dicts/files above are shared across
# request-handling threads -- guard creation and writes with one lock.
_log_lock = threading.Lock()


def _sanitize_id(run_id: str) -> str:
    # run_id comes from the browser's own timestamp string, not path
    # components -- still sanitize before using it in a filename to avoid
    # writing outside LOG_DIR if a malformed/malicious value ever arrives.
    return "".join(c for c in run_id if c.isalnum() or c in "-_") or "run"


def _log_writer(run_id: str) -> csv.writer:
    safe_id = _sanitize_id(run_id)
    if safe_id not in _open_logs:
        path = os.path.join(LOG_DIR, f"web_hand_{safe_id}.csv")
        f = open(path, "w", newline="")
        w = csv.writer(f)
        w.writerow(["t", "measure_index", "found", "x", "y", "nx", "ny"])
        _open_files[safe_id] = f
        _open_logs[safe_id] = w
        print(f"Logging run {safe_id} to {path}")
    return _open_logs[safe_id]


def _score_writer(run_id: str) -> csv.writer:
    # v5 Phase 1 change 4: per-beat scores, in their own file rather than
    # jammed into the per-sample log (different granularity -- one row per
    # beat, not one row per video frame). v6 (2026-07-30): scoring is ictus
    # TIMING (offset_ms, tier), not shape-distance -- see index.html's
    # scoreIctus().
    safe_id = _sanitize_id(run_id)
    if safe_id not in _open_score_logs:
        path = os.path.join(LOG_DIR, f"web_hand_{safe_id}_scores.csv")
        f = open(path, "w", newline="")
        w = csv.writer(f)
        w.writerow(["measure_index", "offset_ms", "tier", "ictus_ny"])
        _open_score_files[safe_id] = f
        _open_score_logs[safe_id] = w
        print(f"Logging scores for run {safe_id} to {path}")
    return _open_score_logs[safe_id]


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIR, **kwargs)

    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        super().end_headers()

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        return json.loads(self.rfile.read(length))

    def _respond_ok(self):
        self.send_response(200)
        self.end_headers()

    def do_POST(self):
        if self.path == "/log":
            data = self._read_json()
            safe_id = "".join(c for c in str(data.get("run_id", "run")) if c.isalnum() or c in "-_") or "run"
            # Audit D1: the browser now batches samples client-side into one
            # POST every ~0.5s instead of one per frame -- "samples" is a
            # list; kept backward-compatible with a single unbatched sample
            # (any old client/replay tooling that posts the legacy shape).
            samples = data.get("samples")
            if samples is None:
                samples = [data]
            with _log_lock:
                w = _log_writer(safe_id)
                for s in samples:
                    w.writerow([
                        s.get("t"), s.get("measure_index"), s.get("found"),
                        s.get("x"), s.get("y"), s.get("nx"), s.get("ny"),
                    ])
                if samples:
                    _open_files[safe_id].flush()
            self._respond_ok()
        elif self.path == "/score":
            data = self._read_json()
            safe_id = "".join(c for c in str(data.get("run_id", "run")) if c.isalnum() or c in "-_") or "run"
            with _log_lock:
                w = _score_writer(safe_id)
                w.writerow([data.get("measure_index"), data.get("offset_ms"), data.get("tier"), data.get("ictus_ny")])
                _open_score_files[safe_id].flush()
            self._respond_ok()
        elif self.path == "/snapshot":
            data = self._read_json()
            run_id = "".join(c for c in str(data.get("run_id", "run")) if c.isalnum() or c in "-_") or "run"
            image_data = data.get("image", "")
            if image_data.startswith("data:image/png;base64,"):
                image_data = image_data.split(",", 1)[1]
            path = os.path.join(LOG_DIR, f"web_hand_{run_id}_trace_final.png")
            with open(path, "wb") as f:
                f.write(base64.b64decode(image_data))
            print(f"Snapshot saved: {path}")
            self._respond_ok()
        else:
            self.send_response(404)
            self.end_headers()


if __name__ == "__main__":
    # Audit D1: plain socketserver.TCPServer handles one request at a time --
    # with per-frame /log POSTs (pre-batching) that meant every static-asset
    # fetch and every CSV write serialized behind the video-tracking loop's
    # own fetch()es, a likely real cause of "tracking felt slow." Batching
    # (see index.html's flushLogSamples()) cuts the request rate; threading
    # removes the remaining head-of-line blocking against page/asset loads.
    with http.server.ThreadingHTTPServer(("", PORT), Handler) as httpd:
        print(f"Serving {DIR} at http://localhost:{PORT}/  (Ctrl+C to stop)")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            for f in _open_files.values():
                f.close()
            for f in _open_score_files.values():
                f.close()
