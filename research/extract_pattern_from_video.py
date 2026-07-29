"""v5 Phase 2: derive real 2/4, 3/4, 4/4 conducting-pattern keyframes from
user-supplied reference videos (2_4.mp4, 3_4.mp4, 4_4.mp4 at repo root).

IMPORTANT (discovered by inspecting frames, not assumed): these are NOT
camera recordings of a real hand -- they're generated animations of the
conducting curve being drawn, with a numeral label ("1", "2", ...)
appearing at each beat's ictus point. MediaPipe hand tracking finds
nothing in them (confirmed: 0/103 frames detected on 2_4.mp4).

A first version of this script tried to color-segment the curve into one
solid color per stroke (visually there do appear to be ~4 distinct pastel
hues in 4/4's frame). That was WRONG: k-means on pixel color instead
split the path by a continuous color GRADIENT along its arc length, which
does not align with beat boundaries at all -- confirmed by inspecting the
per-cluster pixel bounding boxes, e.g. 4/4's "red" cluster stopped partway
down the first straight stroke (y 112-677) while a second cluster picked
up the rest of that SAME straight stroke (y 677-1097) fused together with
the next stroke entirely, because the gradient crossed a k-means bucket
boundary mid-stroke, not at the beat vertex.

Correct approach (verified below): the whole curve is ONE connected
component regardless of color; the digit labels ("1".."k") are each their
own small, disconnected component -- confirmed via connectedComponents on
the raw non-background mask: exactly `beats_per_measure` small components
plus one large one, for all three videos. So:
  1. Take the largest connected component as the curve path.
  2. Take every other component as a beat-number label.
  3. BFS geodesic distance (pixel-adjacency, not Euclidean) from the
     curve's topmost pixel (prep -- unambiguous, nothing else is drawn
     that high yet at any frame) orders every curve pixel by arc-length
     from prep.
  4. Each label's vertex = the curve pixel nearest to that label's
     centroid; sorting labels by THEIR geodesic distance (not by reading
     the digit) recovers the correct beat order without OCR.

Usage:
    python research/extract_pattern_from_video.py
"""

import csv
import os

import cv2
import numpy as np

REPO_ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT_DIR = os.path.join(os.path.dirname(__file__), "captures")
os.makedirs(OUT_DIR, exist_ok=True)

VIDEOS = {2: "2_4.mp4", 3: "3_4.mp4", 4: "4_4.mp4"}
BG_DIST_THRESH = 60  # sum of (255-channel) over BGR; near-white background is ~0


def curve_mask(frame: np.ndarray) -> np.ndarray:
    b, g, r = frame[:, :, 0].astype(int), frame[:, :, 1].astype(int), frame[:, :, 2].astype(int)
    dist_from_white = (255 - b) + (255 - g) + (255 - r)
    return (dist_from_white > BG_DIST_THRESH).astype(np.uint8) * 255


MIN_COMPONENT_AREA = 100  # drops single-pixel/anti-aliasing noise components


def all_frames(path: str) -> list:
    cap = cv2.VideoCapture(path)
    frames = []
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        frames.append(frame)
    cap.release()
    return frames


def extract_chain(frames: list, beats: int):
    """Two problems ruled out a purely-geometric approach on the final
    frame alone (see module docstring): color-gradient clustering splits
    strokes at the wrong place, and connectivity-based (BFS) arc-length
    ordering gets scrambled where strokes visually cross (4/4's left/right
    strokes cross near the middle -- a real feature of that pattern, not a
    rendering glitch). Both problems disappear once you use the fact this
    is a progressively-drawn ANIMATION: each label is drawn at a specific
    frame, in increasing beat order, so the true beat order is recovered
    from *when* each label first appears -- no geometry or OCR needed."""
    masks = [curve_mask(fr) for fr in frames]  # computed once, reused below
    pixel_counts = [int((m > 0).sum()) for m in masks]

    # The very last frame or two can fade to blank (confirmed: 2_4.mp4's
    # true last frame has 0 curve pixels) -- use whichever frame has the
    # most drawn content instead of assuming it's literally the last one.
    final_i = int(np.argmax(pixel_counts))
    final, mask_final = frames[final_i], masks[final_i]
    n_labels, cc, stats, cent = cv2.connectedComponentsWithStats(mask_final, connectivity=8)
    areas = stats[1:, cv2.CC_STAT_AREA]
    order = [i for i in np.argsort(-areas) if areas[i] >= MIN_COMPONENT_AREA]

    curve_id = 1 + int(order[0])
    curve_pts = np.column_stack(np.where(cc == curve_id))

    label_ids = [1 + int(i) for i in order[1:1 + beats]]
    if len(label_ids) != beats:
        print(f"  WARNING: expected {beats} label components, found {len(label_ids)} "
              f"(areas: {sorted(areas, reverse=True)})")
    label_boxes = [stats[i] for i in label_ids]  # x, y, w, h, area
    label_centroids = [cent[i] for i in label_ids]  # (cx, cy)

    # First frame each label's bounding-box region is >= half "drawn in".
    first_seen = []
    for x, y, w, h, area in label_boxes:
        seen_at = len(frames) - 1  # fallback: assume it's the last one
        for fi, m in enumerate(masks):
            region = m[y:y + h, x:x + w]
            if (region > 0).sum() >= 0.5 * area:
                seen_at = fi
                break
        first_seen.append(seen_at)
    beat_order = np.argsort(first_seen)

    beat_vertices_xy = []
    for idx in beat_order:
        cx_lbl, cy_lbl = label_centroids[idx]
        d2 = (curve_pts[:, 0] - cy_lbl) ** 2 + (curve_pts[:, 1] - cx_lbl) ** 2
        ny, nx = curve_pts[np.argmin(d2)]
        beat_vertices_xy.append((float(nx), float(ny)))

    # Prep: wherever the curve FIRST has any content at all (before any
    # label exists) -- avoids assuming prep is the topmost pixel, which
    # broke on 4/4 where beat 4's rebound ends up nearly as high as prep.
    prep_xy = None
    for m in masks:
        pts = np.column_stack(np.where(m > 0))
        if len(pts) > 5:
            cy, cx = pts[:, 0].mean(), pts[:, 1].mean()
            d2 = (curve_pts[:, 0] - cy) ** 2 + (curve_pts[:, 1] - cx) ** 2
            ny, nx = curve_pts[np.argmin(d2)]
            prep_xy = (float(nx), float(ny))
            break

    return [prep_xy] + beat_vertices_xy, mask_final


def to_logical(chain_xy: list, mirror_x: bool):
    """Recenters on prep (chain[0]), converts image (x-right,y-down) to
    logical (x-right,y-up), applies mirror_x if requested, and normalizes
    so the max coordinate magnitude is 1.0 -- matches
    conducting_patterns.py's existing convention."""
    px, py = chain_xy[0]
    pts = []
    for x, y in chain_xy:
        lx = -(x - px) if mirror_x else (x - px)
        ly = -(y - py)
        pts.append((lx, ly))
    max_extent = max(max(abs(x), abs(y)) for x, y in pts) or 1.0
    return [(x / max_extent, y / max_extent) for x, y in pts]


def plot_overlay(frame, chain_xy, out_path, title):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, ax = plt.subplots(figsize=(5, 5))
    ax.imshow(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
    for i, (x, y) in enumerate(chain_xy):
        label = "prep" if i == 0 else f"beat {i}"
        ax.plot(x, y, "o", color="black", markersize=8)
        ax.annotate(label, (x, y), textcoords="offset points", xytext=(8, -8), color="black", fontsize=9, weight="bold")
    xs = [p[0] for p in chain_xy]
    ys = [p[1] for p in chain_xy]
    ax.plot(xs, ys, "-", color="black", alpha=0.4, linewidth=1)
    ax.set_title(title)
    fig.tight_layout()
    fig.savefig(out_path, dpi=120)
    plt.close(fig)


def main():
    raw_chains = {}
    for sig, fname in VIDEOS.items():
        path = os.path.join(REPO_ROOT, fname)
        print(f"Processing {fname} ...")
        frames = all_frames(path)
        chain_xy, mask = extract_chain(frames, sig)
        print(f"  chain (image px): {[tuple(round(v, 1) for v in p) for p in chain_xy]}")

        overlay_path = os.path.join(OUT_DIR, f"pattern_{sig}4_overlay.png")
        plot_overlay(frames[-1], chain_xy, overlay_path, f"{sig}/4 extracted from {fname}")
        print(f"  overlay: {overlay_path}")

        csv_path = os.path.join(OUT_DIR, f"pattern_{sig}4_vertices_px.csv")
        with open(csv_path, "w", newline="") as f:
            wcsv = csv.writer(f)
            wcsv.writerow(["label", "x_px", "y_px"])
            for i, (x, y) in enumerate(chain_xy):
                wcsv.writerow(["prep" if i == 0 else f"beat{i}", x, y])
        print(f"  vertices logged: {csv_path}")

        raw_chains[sig] = chain_xy

    # Mirror-convention control on 2/4: the user's verified real hand-drawn
    # diagram says beat 2 rebounds up-and-RIGHT of beat 1.
    chain2 = raw_chains[2]
    for mirror in (False, True):
        pts = to_logical(chain2, mirror_x=mirror)
        beat2_x = pts[2][0]
        print(f"2/4 mirror_x={mirror}: prep={pts[0]}, beat1={pts[1]}, beat2={pts[2]} "
              f"-> beat2 is {'RIGHT' if beat2_x > 0 else 'LEFT'} of prep")

    print("\nAll three overlays written to research/captures/pattern_*4_overlay.png -- "
          "inspect them before trusting the vertices.")


if __name__ == "__main__":
    main()
