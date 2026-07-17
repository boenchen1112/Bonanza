"""Build Plan v3 Phase 1, input fallback option 2: streams JoyConStream
samples to a local Unity process over UDP, decoupling the Unity port from
HID/library debugging entirely. Reuses the exact, already-validated
joycon_stream.py pipeline (POLL_INTERVAL_S, duplicate-read skip) as the
single source of truth for polling/timestamping -- Unity only ever
consumes (timestamp, gx, gy, gz), never touches HID itself.

Wire format: one UDP datagram per sample, JSON body:
    {"t": <perf_counter float>, "gx": <float>, "gy": <float>, "gz": <float>}

`t` is this process's time.perf_counter() clock -- NOT comparable to
Unity's clocks. The Unity receiver must timestamp each datagram on arrival
with its own canonical clock (see Phase 3's clock-domain rule) rather than
trusting `t` as wall-clock-equivalent; `t` is included only so inter-sample
dt on the Python side can be cross-checked if needed.

Usage:
    python joycon_udp_bridge.py --seconds 60 --host 127.0.0.1 --port 9999
"""

import argparse
import json
import socket
import sys

from joycon_stream import JoyConStream


def run_bridge(duration_s: float, host: str, port: int) -> None:
    try:
        stream = JoyConStream()
    except RuntimeError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    addr = (host, port)
    sent = 0
    print(f"Streaming to {host}:{port} for {duration_s}s ... swing the Joy-Con now.")
    try:
        for t, gx, gy, gz, _mag in stream.stream(duration_s):
            packet = json.dumps({"t": t, "gx": gx, "gy": gy, "gz": gz}).encode("utf-8")
            sock.sendto(packet, addr)
            sent += 1
    finally:
        sock.close()
    print(f"Done. Sent {sent} samples.")


def main():
    parser = argparse.ArgumentParser(description="Bridge right-Joy-Con gyro data to Unity over UDP.")
    parser.add_argument("--seconds", type=float, default=60.0)
    parser.add_argument("--host", type=str, default="127.0.0.1")
    parser.add_argument("--port", type=int, default=9999)
    args = parser.parse_args()
    run_bridge(args.seconds, args.host, args.port)


if __name__ == "__main__":
    main()
