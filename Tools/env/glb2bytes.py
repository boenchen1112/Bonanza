"""Web authored GLB (main branch: web/src/assets/env/sk-skyline.glb) -> Resources/Env/<mesh>.bytes for EnvBytesMesh.
# python3 Tools/env/glb2bytes.py <in.glb> Assets/Game/Resources/Env sk-   (needs numpy)
Three.js space -> Unity: z negated, triangle winding reversed. Node transforms applied. Layout: see EnvBytesMesh.cs."""
import json, struct, sys
from pathlib import Path
import numpy as np

src = Path(sys.argv[1]); out = Path(sys.argv[2]); prefix = sys.argv[3]
data = src.read_bytes()
assert data[:4] == b"glTF"
off = 12; js = None; bin_ = None
while off < len(data):
    ln, typ = struct.unpack_from("<II", data, off); chunk = data[off + 8: off + 8 + ln]
    if typ == 0x4E4F534A: js = json.loads(chunk)
    elif typ == 0x004E4942: bin_ = chunk
    off += 8 + ln
CT = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
def acc(i):
    a = js["accessors"][i]; bv = js["bufferViews"][a["bufferView"]]
    dt = np.dtype(CT[a["componentType"]]); n = NC[a["type"]]
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride", 0) or dt.itemsize * n
    raw = np.frombuffer(bin_, dtype=np.uint8, count=stride * (a["count"] - 1) + dt.itemsize * n, offset=start)
    arr = np.lib.stride_tricks.as_strided(raw, shape=(a["count"], dt.itemsize * n), strides=(stride, 1)).copy()
    v = arr.view(dt).reshape(a["count"], n).astype(np.float64 if dt == np.float32 else np.int64)
    if a.get("normalized") and dt != np.float32: v = v / np.iinfo(dt).max
    return v
def mat_of(node):
    m = np.eye(4)
    if "matrix" in node: return np.array(node["matrix"]).reshape(4, 4).T
    t = node.get("translation", [0, 0, 0]); r = node.get("rotation", [0, 0, 0, 1]); s = node.get("scale", [1, 1, 1])
    x, y, z, w = r
    R = np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
    m[:3, :3] = R * np.array(s); m[:3, 3] = t
    return m
def walk(ni, parent):
    node = js["nodes"][ni]; m = parent @ mat_of(node)
    if "mesh" in node: yield node.get("name", js["meshes"][node["mesh"]].get("name")), node["mesh"], m
    for c in node.get("children", []): yield from walk(c, m)
scene = js["scenes"][js.get("scene", 0)]
for root in scene["nodes"]:
    for name, mi, m in walk(root, np.eye(4)):
        P, N, C, I = [], [], [], []; base = 0
        for prim in js["meshes"][mi]["primitives"]:
            at = prim["attributes"]
            p = acc(at["POSITION"]); n = acc(at["NORMAL"])
            c = acc(at["COLOR_0"])[:, :3] if "COLOR_0" in at else np.ones_like(p)
            idx = acc(prim["indices"]).reshape(-1) if "indices" in prim else np.arange(len(p))
            p = (m @ np.c_[p, np.ones(len(p))].T).T[:, :3]
            nm = np.linalg.inv(m[:3, :3]).T; n = (nm @ n.T).T; n /= np.linalg.norm(n, axis=1, keepdims=True)
            P.append(p); N.append(n); C.append(c); I.append(idx + base); base += len(p)
        P = np.concatenate(P); N = np.concatenate(N); C = np.concatenate(C); I = np.concatenate(I).reshape(-1, 3)
        P[:, 2] *= -1; N[:, 2] *= -1; I = I[:, [0, 2, 1]]
        fn = out / f"{prefix}{name.lower()}.bytes"
        with open(fn, "wb") as f:
            f.write(b"BNZM"); f.write(struct.pack("<ii", len(P), I.size))
            for a in (P, N, C): f.write(a.astype("<f4").tobytes())
            f.write(I.astype("<i4").tobytes())
        print(fn.name, "verts", len(P), "tris", len(I), "bounds", P.min(0).round(1), P.max(0).round(1), "col", C.min(0).round(2), C.max(0).round(2))
