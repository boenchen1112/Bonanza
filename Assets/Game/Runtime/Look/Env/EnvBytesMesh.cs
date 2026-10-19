// Authored set meshes shipped as raw arrays in Resources/Env/<name>.bytes (a TextAsset), so the player needs
// no glTF importer and the coordinates are already Unity's. Layout, little-endian:
//   "BNZM", int32 vertexCount, int32 indexCount,
//   float32 position[3 * v], float32 normal[3 * v], float32 colour[3 * v] (linear), int32 index[i]
// Written by Tools/env/glb2bytes.py from the web branch's authored GLBs (web/src/assets/env; z flipped, winding reversed).

using UnityEngine;
using UnityEngine.Rendering;

namespace Bonanza.Look
{
    public static class EnvBytesMesh
    {
        public static Mesh Load(string name)
        {
            var ta = Resources.Load<TextAsset>("Env/" + name);
            if (ta == null) { Debug.LogWarning("[ENV] missing mesh Env/" + name); return null; }
            var d = ta.bytes;
            if (d.Length < 12 || d[0] != 'B' || d[1] != 'N' || d[2] != 'Z' || d[3] != 'M') { Debug.LogWarning("[ENV] bad mesh Env/" + name); return null; }
            int v = System.BitConverter.ToInt32(d, 4), n = System.BitConverter.ToInt32(d, 8);
            int o = 12;
            var p = new Vector3[v]; var nr = new Vector3[v]; var c = new Color[v]; var idx = new int[n];
            for (int i = 0; i < v; i++, o += 12) p[i] = new Vector3(F(d, o), F(d, o + 4), F(d, o + 8));
            for (int i = 0; i < v; i++, o += 12) nr[i] = new Vector3(F(d, o), F(d, o + 4), F(d, o + 8));
            for (int i = 0; i < v; i++, o += 12) c[i] = new Color(F(d, o), F(d, o + 4), F(d, o + 8), 1);
            for (int i = 0; i < n; i++, o += 4) idx[i] = System.BitConverter.ToInt32(d, o);
            var m = new Mesh { name = name };
            if (v > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = p; m.normals = nr; m.colors = c;
            m.uv = new Vector2[v];
            m.triangles = idx;
            m.RecalculateBounds();
            m.UploadMeshData(true);
            Resources.UnloadAsset(ta);
            return m;
        }

        /// Just the vertex positions of an authored mesh (the uploaded Mesh is not CPU-readable): for placing
        /// dressing on it at load, e.g. lights on the skyline's roofs. Null if missing.
        public static Vector3[] Positions(string name)
        {
            var ta = Resources.Load<TextAsset>("Env/" + name);
            if (ta == null) return null;
            var d = ta.bytes;
            if (d.Length < 12 || d[0] != 'B' || d[1] != 'N' || d[2] != 'Z' || d[3] != 'M') return null;
            int v = System.BitConverter.ToInt32(d, 4);
            var p = new Vector3[v];
            for (int i = 0, o = 12; i < v; i++, o += 12) p[i] = new Vector3(F(d, o), F(d, o + 4), F(d, o + 8));
            Resources.UnloadAsset(ta);
            return p;
        }

        /// Positions, normals and the index list of an authored mesh (CPU side): for building dressing that follows
        /// its faces at load, e.g. window grids on the skyline's camera-facing facades. False if missing.
        public static bool Read(string name, out Vector3[] p, out Vector3[] n, out int[] idx)
        {
            p = n = null; idx = null;
            var ta = Resources.Load<TextAsset>("Env/" + name);
            if (ta == null) return false;
            var d = ta.bytes;
            if (d.Length < 12 || d[0] != 'B' || d[1] != 'N' || d[2] != 'Z' || d[3] != 'M') return false;
            int v = System.BitConverter.ToInt32(d, 4), ni = System.BitConverter.ToInt32(d, 8);
            int o = 12;
            p = new Vector3[v]; n = new Vector3[v]; idx = new int[ni];
            for (int i = 0; i < v; i++, o += 12) p[i] = new Vector3(F(d, o), F(d, o + 4), F(d, o + 8));
            for (int i = 0; i < v; i++, o += 12) n[i] = new Vector3(F(d, o), F(d, o + 4), F(d, o + 8));
            o += 12 * v;   // colours
            for (int i = 0; i < ni; i++, o += 4) idx[i] = System.BitConverter.ToInt32(d, o);
            Resources.UnloadAsset(ta);
            return true;
        }

        static float F(byte[] d, int o) => System.BitConverter.ToSingle(d, o);
    }
}
