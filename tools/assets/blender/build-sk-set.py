"""
Swing Kings set dressing: the skyline beyond the ballpark and the set edge
around the stands (ticket 27 — critic round 6's "the set edge beyond the
stands and the skyline are blockout").

    node tools/assets/blender/run-blender.mjs tools/assets/blender/build-sk-set.py

Writes web/src/assets/env/sk-skyline.glb directly (pass `-- <out.glb>` to
write somewhere else while iterating). Deterministic: every placement comes
from a fixed-seed random.Random, nothing is sculpted by hand, so re-running
this script IS the record of how the asset was made.

Two meshes, one draw call each in game (swingKings/world.js swaps in house
toon materials; the materials exported here are placeholders):

  skyline   a varied stadium-city silhouette on an arc behind the backdrop
            arches — setback towers, a dome, a water tower, a crane, masts,
            distant light towers, over a continuous low-rise band. Vertex
            colour is a VALUE only (grey): the hue comes from the material,
            which the game tints to the palette's band/fog every frame, so it
            stays "one shade above the fog" by day and by night. Low and
            plain behind the middle of the frame (where the pitch arcs and
            the pips live); the tall, detailed shapes are on the flanks.
  setEdge   what the camera sees past the end of the stands: a stepped end
            block capping each open end of the stand shell, a pylon that
            lands each end of the floating roof ring, a parapet along the
            top of the stands, a padded outfield wall, and a concourse apron
            from the grass edge outwards so no sky shows below the horizon.
            Vertex colour is the real (sRGB-authored) stadium colour.

Everything is authored directly in three.js WORLD coordinates of the Swing
Kings scene (Y up, camera on +Z) and converted to Blender's Z-up at vertex
creation; the glTF exporter's +Y-up conversion turns it back, so the game
adds the meshes at the origin with no transform. The layout numbers below
mirror swingKings/world.js (stands: radius 11 -> 18.4 around (0, -3), arc
1.02 pi; roof ring radius 18.6 at y 7.4; grass disc radius 13 at the origin).
Flat-shaded, low-poly, chamfered boxes and faceted prisms — the same visual
language as the procedural set (arches, stands, towers), not realism.
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(ROOT, 'web', 'src', 'assets', 'env', 'sk-skyline.glb')
if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 1:
    OUT = os.path.abspath(sys.argv[sys.argv.index('--') + 1])

SEED = 0x5C1E

# --- Swing Kings layout (keep in step with web/src/games/swingKings/world.js)
STAND_CZ = -3.0          # stands/roof centre z
STAND_R0, STAND_R1 = 11.0, 18.4
STAND_Y0, STAND_Y1 = 0.6, 4.5     # slope bottom/top (2.55 -+ 3.9/2)
ARC = math.pi * 1.02
ROOF_R, ROOF_Y = 18.6, 7.4
GRASS_R = 13.0


# ------------------------------------------------------------------ colour
def srgb_to_linear(c):
    """glTF vertex colours are linear; every hex in this repo is sRGB."""
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rgba(h):
    r, g, b = ((h >> 16) & 255) / 255, ((h >> 8) & 255) / 255, (h & 255) / 255
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)


def grey(v):
    v = max(0.0, min(1.0, v))
    lv = srgb_to_linear(v)
    return (lv, lv, lv, 1.0)


# --------------------------------------------------------------- geometry
def P(x, y, z):
    """three.js world (Y up, +Z to camera) -> Blender (Z up)."""
    return Vector((x, -z, y))


def rot_y(a):
    """A three.js rotation.y of `a` is a Blender rotation about Z by `a`."""
    return Matrix.Rotation(a, 4, 'Z')


class Kit:
    """One bmesh + one corner colour layer; every primitive is painted as it
    is added, so the whole piece exports as a single mesh (one draw call)."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new('Col')

    def _paint(self, first, color, top=None, down=None):
        self.bm.faces.ensure_lookup_table()
        for f in self.bm.faces[first:]:
            f.normal_update()
            c = color
            if top is not None and f.normal.z > 0.7:
                c = top
            elif down is not None and f.normal.z < -0.7:
                c = down
            for loop in f.loops:
                loop[self.col] = c

    def box(self, x, y0, z, w, h, d, color, rot=0.0, chamfer=0.0, top=None):
        """Axis box, base at y0, w along x, d along z (before `rot`)."""
        first = len(self.bm.faces)
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = res['verts']
        m = Matrix.Translation(P(x, y0 + h / 2, z)) @ rot_y(rot) @ Matrix.Diagonal((w, d, h, 1.0))
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        if chamfer > 0:
            edges = list({e for v in verts for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=chamfer, offset_type='OFFSET',
                            segments=1, profile=0.5, affect='EDGES', clamp_overlap=True)
        self._paint(first, color, top)

    def prism(self, x, y0, z, r1, r2, h, segs, color, rot=0.0, top=None, caps=True):
        """Faceted frustum (r2 = 0: a cone), base at y0."""
        first = len(self.bm.faces)
        m = Matrix.Translation(P(x, y0 + h / 2, z)) @ rot_y(rot)
        bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segs,
                              radius1=r1, radius2=r2, depth=h, matrix=m)
        self._paint(first, color, top)

    def dome(self, x, y0, z, r, segs, bands, color, top=None):
        """Low-poly dome as stacked frusta (faceted, like everything else)."""
        for k in range(bands):
            a0 = (k / bands) * math.pi / 2
            a1 = ((k + 1) / bands) * math.pi / 2
            r1, r2 = r * math.cos(a0), r * math.cos(a1)
            yb = y0 + r * math.sin(a0)
            h = r * (math.sin(a1) - math.sin(a0))
            self.prism(x, yb, z, r1, max(r2, 0.0), h, segs, color if k < bands - 1 else (top or color),
                       caps=(k == bands - 1))

    def ring_strip(self, cx, cz, r_in, r_out, y, a0, a1, segs, color):
        """Flat annular sector at height y (angles: three's theta, x = r sin, z = r cos)."""
        first = len(self.bm.faces)
        bm = self.bm
        inner, outer = [], []
        for i in range(segs + 1):
            t = a0 + (a1 - a0) * i / segs
            s, c = math.sin(t), math.cos(t)
            inner.append(bm.verts.new(P(cx + r_in * s, y, cz + r_in * c)))
            outer.append(bm.verts.new(P(cx + r_out * s, y, cz + r_out * c)))
        for i in range(segs):
            f = bm.faces.new((inner[i], outer[i], outer[i + 1], inner[i + 1]))
            f.normal_update()
            if f.normal.z < 0:
                f.normal_flip()
        self._paint(first, color)

    def finish(self, mat_name):
        me = bpy.data.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        for p in me.polygons:
            p.use_smooth = False
        attr = me.color_attributes.get('Col')
        me.color_attributes.active_color = attr
        try:
            me.color_attributes.render_color_index = me.color_attributes.active_color_index
        except AttributeError:
            pass
        mat = bpy.data.materials.new(mat_name)
        me.materials.append(mat)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def lerp(a, b, t):
    return a + (b - a) * t


# ================================================================= skyline
def build_skyline(rng):
    k = Kit('skyline')
    CX, CZ = 0.0, STAND_CZ

    def on_arc(r, phi_deg):
        p = math.radians(phi_deg)
        return CX + r * math.sin(p), CZ - r * math.cos(p), -p   # x, z, facing rot

    def v(base):
        return grey(base + rng.uniform(-0.05, 0.05))

    def tower(r, phi, w, d, h, windows):
        x, z, rot = on_arc(r, phi)
        rot += rng.uniform(-0.18, 0.18)
        val = rng.uniform(0.66, 0.82)
        col, top = grey(val), grey(min(1.0, val + 0.14))
        tiers = rng.choice((1, 2, 2, 3))
        y, cw, cd = 0.0, w, d
        remaining = h
        for t in range(tiers):
            th = remaining if t == tiers - 1 else remaining * rng.uniform(0.55, 0.7)
            k.box(x, y, z, cw, th, cd, col, rot=rot, chamfer=min(cw, cd) * 0.07, top=top)
            if windows:
                # Window bands on the face toward the stadium: slightly
                # lighter strips, same hue — detail by value, never by accent.
                wc = grey(min(1.0, val + 0.13))
                rows = int(th // 1.9)
                for row in range(rows):
                    yy = y + 1.0 + row * 1.9
                    if yy + 0.45 > y + th - 0.4:
                        break
                    ox, oz = 0.0, cd / 2 + 0.04
                    k.box(x + ox * math.cos(rot) + oz * math.sin(rot), yy,
                          z - ox * math.sin(rot) + oz * math.cos(rot),
                          cw * 0.74, 0.45, 0.1, wc, rot=rot)
            y += th
            remaining -= th
            cw *= rng.uniform(0.62, 0.8)
            cd *= rng.uniform(0.62, 0.8)
        crown = rng.choice(('spire', 'hip', 'flat', 'mast'))
        if crown == 'spire':
            k.prism(x, y, z, cw * 0.32, 0.0, rng.uniform(2.5, 5.0), 4, top, rot=rot + math.pi / 4)
        elif crown == 'hip':
            k.prism(x, y, z, max(cw, cd) * 0.62, max(cw, cd) * 0.18, rng.uniform(1.0, 1.8), 4, top,
                    rot=rot + math.pi / 4)
        elif crown == 'flat':
            k.box(x + rng.uniform(-0.3, 0.3) * cw, y, z, cw * 0.4, 0.9, cd * 0.4, col, rot=rot, chamfer=0.08)
        else:
            k.box(x, y, z, 0.18, rng.uniform(2.5, 4.5), 0.18, top, rot=rot)

    def water_tower(r, phi, s):
        x, z, rot = on_arc(r, phi)
        col, top = grey(0.74), grey(0.9)
        leg_h = 7.5 * s
        for lx, lz in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            k.box(x + lx * 1.0 * s, 0.0, z + lz * 1.0 * s, 0.22 * s, leg_h, 0.22 * s, col, rot=rot)
        k.box(x, leg_h * 0.55, z, 2.3 * s, 0.18 * s, 2.3 * s, col, rot=rot)   # brace
        k.prism(x, leg_h, z, 1.9 * s, 1.9 * s, 2.6 * s, 10, col, top=top)
        k.prism(x, leg_h + 2.6 * s, z, 2.05 * s, 0.25 * s, 1.1 * s, 10, top)

    def crane(r, phi, h):
        x, z, rot = on_arc(r, phi)
        col = grey(0.8)
        k.box(x, 0.0, z, 0.8, h, 0.8, col, rot=rot)
        k.box(x, h, z, 0.9, 1.1, 1.0, grey(0.7), rot=rot)                       # cab
        # jib out over the city, counter-jib + weight the other way
        jl, cl = 13.0, 4.5
        c, s_ = math.cos(rot + math.pi / 2), math.sin(rot + math.pi / 2)
        k.box(x + (jl / 2 - 0.5) * c, h + 1.1, z - (jl / 2 - 0.5) * s_, jl, 0.45, 0.45, col,
              rot=rot + math.pi / 2)
        k.box(x - (cl / 2) * c, h + 1.1, z + (cl / 2) * s_, cl, 0.45, 0.45, col, rot=rot + math.pi / 2)
        k.box(x - (cl - 0.8) * c, h + 0.1, z + (cl - 0.8) * s_, 1.2, 1.0, 0.9, grey(0.66),
              rot=rot + math.pi / 2)
        k.prism(x, h + 1.1, z, 0.35, 0.0, 2.6, 4, col, rot=rot + math.pi / 4)  # apex

    def light_tower(r, phi, h):
        x, z, rot = on_arc(r, phi)
        col = grey(0.78)
        k.prism(x, 0.0, z, 0.45, 0.25, h, 6, col)
        k.box(x, h - 0.2, z, 4.4, 2.2, 0.5, grey(0.9), rot=rot, chamfer=0.1)
        k.box(x, h - 0.8, z, 2.2, 0.6, 0.6, col, rot=rot)

    def radio_mast(r, phi, h):
        x, z, rot = on_arc(r, phi)
        col = grey(0.8)
        k.prism(x, 0.0, z, 0.55, 0.08, h, 4, col, rot=rot + math.pi / 4)
        for f in (0.35, 0.62, 0.84):
            k.box(x, h * f, z, 2.2 * (1.1 - f), 0.16, 0.16, col, rot=rot)

    # --- 1. the low-rise band: a continuous base just above the stand tops,
    # so the horizon reads as "city" everywhere, not "sky with some boxes".
    phi = -56.0
    while phi <= 56.0:
        w = rng.uniform(3.6, 6.0)
        x, z, rot = on_arc(74.0 + rng.uniform(-2, 2), phi)
        h = rng.uniform(5.5, 8.5) + (abs(phi) / 56.0) * rng.uniform(0.0, 3.5)
        val = rng.uniform(0.58, 0.7)
        k.box(x, 0.0, z, w, h, rng.uniform(3.5, 6.0), grey(val), rot=rot + rng.uniform(-0.12, 0.12),
              chamfer=0.25, top=grey(val + 0.1))
        if rng.random() < 0.35:
            k.box(x + rng.uniform(-1, 1), h, z, w * 0.35, rng.uniform(0.8, 1.6), 1.2, grey(val),
                  rot=rot, chamfer=0.08)
        phi += math.degrees(w / 74.0) + rng.uniform(0.2, 1.4)

    # --- 2. the middle third: low and plain (the pitch arc and its gold pips
    # cross this band — nothing here may compete with them).
    dx, dz, _ = on_arc(70.0, -15.0)
    k.prism(dx, 0.0, dz, 9.3, 9.3, 2.2, 14, grey(0.64))                       # the arena next door: drum
    k.dome(dx, 2.2, dz, 9.0, 14, 4, grey(0.7), top=grey(0.8))                 # ... and its roof
    k.box(dx, 11.1, dz, 0.3, 2.2, 0.3, grey(0.8))                              # its flagpole
    for p in (-5.0, 3.0, 9.0):
        tower(68.0 + rng.uniform(-2, 2), p + rng.uniform(-1.5, 1.5),
              rng.uniform(5.0, 7.0), rng.uniform(4.0, 6.0), rng.uniform(9.0, 11.5), windows=False)

    # --- 3. flanks: the tall, detailed silhouettes.
    left = [-50.0, -45.0, -38.0, -32.0, -26.0, -21.0]
    right = [20.0, 25.0, 31.0, 37.0, 43.0, 49.0]
    for i, p in enumerate(left + right):
        edge = min(1.0, (abs(p) - 18.0) / 30.0)
        tower(64.0 + rng.uniform(-3, 3), p + rng.uniform(-1.2, 1.2),
              rng.uniform(4.2, 6.5), rng.uniform(4.0, 6.0),
              lerp(12.0, 21.0, edge) + rng.uniform(-2.0, 3.0), windows=True)
    water_tower(60.0, -29.5, 1.15)
    crane(66.0, 34.0, 20.0)
    radio_mast(70.0, -41.5, 27.0)
    radio_mast(71.0, 46.0, 22.0)
    light_tower(58.0, -55.0, 19.0)
    light_tower(58.0, 55.0, 19.0)
    return k.finish('skyline')


# ================================================================ set edge
def build_set_edge(rng):
    k = Kit('setEdge')
    NAVY = rgba(0x22396c)      # stand shell, one step darker than standMat's 0x2c4c82
    NAVY_TOP = rgba(0x2f4f8a)
    STEEL = rgba(0x1c2436)     # roofMat
    STEEL_TOP = rgba(0x2a3450)
    PAD = rgba(0x1b3160)       # outfield wall padding
    CAP = rgba(0xffd93d)       # swing-kings accent: wall cap / stripe
    APRON = rgba(0x1d2a45)     # concourse beyond the grass

    th0 = math.pi - ARC / 2    # right end of the stand shell (x > 0)
    th1 = math.pi + ARC / 2    # left end (x < 0)

    def stand_point(r, th, t=0.0):
        """Point at radius r on stand angle th, offset t along the tangent
        (+t = further round past the end, i.e. toward the camera side)."""
        s, c = math.sin(th), math.cos(th)
        return r * s + t * c, STAND_CZ + r * c - t * s

    # --- end blocks: stepped, following the slope, capping each open end of
    # the stand shell (the raw cut edge the critic called "the set edge").
    DARK = rgba(0x0d1324)      # tunnel mouth
    t0, t1 = -0.3, 1.1         # from just inside the shell to 1.1 past its end
    tm_ = (t0 + t1) / 2
    for th, sign in ((th1, 1.0), (th0, -1.0)):
        rot = th - math.pi / 2          # box local +x runs radially outward
        # Five steps riding just above the slope (y = 0.6 at r 11 -> 4.5 at
        # r 18.4), so the end reads as the stand's own stepped end wall.
        edges_r = [10.6 + (19.3 - 10.6) * i / 5 for i in range(6)]
        for i in range(5):
            r0, r1 = edges_r[i], edges_r[i + 1]
            h = max(1.35, STAND_Y0 + (r1 - STAND_R0) / (STAND_R1 - STAND_R0) * (STAND_Y1 - STAND_Y0) + 0.3)
            x, z = stand_point((r0 + r1) / 2, th, tm_ * sign)
            k.box(x, 0.0, z, r1 - r0 + 0.02, h, t1 - t0, NAVY, rot=rot, chamfer=0.07, top=NAVY_TOP)
            k.box(x, h, z, r1 - r0 + 0.16, 0.18, t1 - t0 + 0.16, STEEL, rot=rot, chamfer=0.04, top=STEEL_TOP)
        # tunnel mouth on the face toward the camera side, under steps 2-3
        x, z = stand_point(14.1, th, (t1 + 0.03) * sign)
        k.box(x, 0.0, z, 2.0, 1.25, 0.08, DARK, rot=rot)
        x, z = stand_point(14.1, th, (t1 + 0.05) * sign)
        k.box(x, 1.25, z, 2.3, 0.14, 0.1, CAP, rot=rot)              # lintel
        # accent stripe on the face toward the field, level with the wall cap
        x, z = stand_point(10.57, th, tm_ * sign)
        k.box(x, 1.15, z, 0.08, 0.14, t1 - t0, CAP, rot=rot)
        # pylon: lands the end of the floating roof ring on the ground
        x, z = stand_point(ROOF_R, th, 0.55 * sign)
        k.box(x, 0.0, z, 0.75, ROOF_Y + 0.35, 0.75, STEEL, rot=rot, chamfer=0.08, top=STEEL_TOP)
        # a light housing on top of the pylon, so the ring END reads as capped
        k.box(x, ROOF_Y + 0.35, z, 1.2, 0.5, 1.2, STEEL, rot=rot, chamfer=0.08, top=STEEL_TOP)

    # --- parapet along the top of the stands: a finished edge instead of the
    # slope's raw cut against the sky. Horizontal only (no posts: the band
    # between the stands and the roof is where the home runs land).
    segs = 40
    for i in range(segs):
        a0 = th0 + ARC * i / segs
        a1 = th0 + ARC * (i + 1) / segs
        am = (a0 + a1) / 2
        rr = STAND_R1 + 0.28
        x, z = rr * math.sin(am), STAND_CZ + rr * math.cos(am)
        chord = 2 * rr * math.sin((a1 - a0) / 2) + 0.03
        # rot = am: box local +z is radial, local +x tangential
        k.box(x, STAND_Y1 - 0.35, z, chord, 0.95, 0.42, STEEL, rot=am, top=STEEL_TOP)

    # --- outfield wall: padded, yellow-capped, from each end block round
    # toward the camera just outside the grass edge.
    for side in (-1.0, 1.0):
        wr = GRASS_R + 0.35
        a_start = math.atan2(side * 1.0, 0.0)   # pointing along +/-x
        n = 18
        for i in range(n):
            # from 4 deg behind the x axis to 70 deg toward the camera
            d0 = math.radians(-4.0 + 74.0 * i / n)
            d1 = math.radians(-4.0 + 74.0 * (i + 1) / n)
            dm = (d0 + d1) / 2
            ang = a_start - side * dm        # three theta: x = r sin, z = r cos
            x, z = wr * math.sin(ang), wr * math.cos(ang)
            chord = 2 * wr * math.sin((d1 - d0) / 2) + 0.02
            k.box(x, 0.0, z, chord, 1.15, 0.32, PAD, rot=ang)
            k.box(x, 1.15, z, chord, 0.12, 0.4, CAP, rot=ang)

    # --- concourse apron: from the grass edge out, all the way round, so the
    # ground never ends in sky (the "flat wedge" at the left of the frame).
    k.ring_strip(0.0, 0.0, GRASS_R + 0.02, 34.0, -0.03, 0.0, math.pi * 2, 64, APRON)
    k.ring_strip(0.0, 0.0, 34.0, 110.0, -0.05, 0.0, math.pi * 2, 64, APRON)
    return k.finish('setEdge')


# =================================================================== build
def build():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    rng = random.Random(SEED)
    sky = build_skyline(rng)
    edge = build_set_edge(random.Random(SEED + 1))

    bpy.ops.object.select_all(action='DESELECT')
    for ob in (sky, edge):
        ob.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=OUT, export_format='GLB', use_selection=True,
        export_yup=True, export_apply=False, export_normals=True,
        export_texcoords=False, export_materials='EXPORT',
        export_vertex_color='ACTIVE', export_animations=False, export_skins=False,
        export_morph=False,
    )
    for ob in (sky, edge):
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        print(f'BUILD_SK_SET part={ob.name} tris={tris}')
    print(f'BUILD_SK_SET_OK out={OUT} bytes={os.path.getsize(OUT)} blender={bpy.app.version_string}')


build()
