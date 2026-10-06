"""Write Assets/Prefabs/boss_stage.prefab: the Bone Colossus's stage, a lava-ringed platform.

    python tools/gen_boss_stage.py [--seed 7]

Reads the demo import (Assets/Prefabs/hell_arena.prefab, the Dungeon Realms Demo_Hell_01 hub) and keeps only its
SURROUNDINGS: the lava sea underneath, the colossal scenery bones, and the big cliff/spike/ground pieces standing
farther than CLEAR from the platform centre. Everything under and around the ritual circle (the demo's hub: plates,
stairs, pillars, obelisks, props, lava rivers, lights) is dropped. The circle itself becomes the platform at the
user's transform (5x wide, 0.82 tall, raised to the spawn height; 2026-09-22), 1500 u radius, and around it the
stage is laid fresh: a ring of lava tiles, tall hell pillars standing in the lava (two of them the hands' resting
pillars), a rock island roost for the skull opposite the spawn, rocks and spike clusters in the lava, and skull
spikes marking the platform's rim. Markers: `spawn` (on the platform, facing the skull), `boss_skull`,
`boss_hand_l`, `boss_hand_r` (the demo's hand rotations and scales, new positions).

Model sizes come from the FBX files (tools/fbx_bounds.py): centimetres x the vmdl import_scale 0.3937 = units.
Every placement is deterministic (seeded, uuid5 guids), so re-running only changes what the code changed.
Lava tiles beyond the inner ring carry the tag `nolight` (HellArena.Light skips their point light).
"""
import argparse
import json
import math
import os
import random
import sys
import uuid

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import fbx_bounds as fb  # noqa: E402
import gen_boss_arena as gba  # noqa: E402
import import_synty_scene as isc  # noqa: E402

SRC = os.path.join(ROOT, "Assets", "Prefabs", "hell_arena.prefab")
OUT = os.path.join(ROOT, "Assets", "Prefabs", "boss_stage.prefab")
FBX_DIR = os.path.join(ROOT, "Assets", "DungeonRealms")
NS = uuid.UUID("9a4e7c2d-3b1f-4d86-a5e0-6c8f2b7d1e93")
IMPORT_SCALE = 0.3937
LAVA_VMAT = "shaders/realmslava.vmat"

# the platform: the demo's ritual circle at the user's transform (boss_test.scene overrides, 2026-09-22)
CIRCLE_MESH = "SM_Env_Hell_Circle_01"
CIRCLE_POS = (2001.97, 58.6614, 135.490326)
CIRCLE_ROT = (0.0, 0.0, -0.733663798, 0.679512643)
CIRCLE_SCALE = (5.0, 5.0, 0.819550991)

# what stays of the demo beyond the cleared disc: only pieces this big (world footprint, units) or these kinds
CLEAR = 2200.0
BIG_FOOTPRINT = 900.0
ALWAYS_KEEP = ("SM_Env_Rock_Cliff", "SM_Env_Rock_Peak", "SM_Env_Hell_Ground_Extended", "SM_Env_Hell_Spikes_06")
SEA_SCALE = 20.0
COLOSSAL = 5.0
BONES = ("SM_Env_Hell_Bone_Skull_01", "SM_Env_Hell_Bone_Hand_L_01", "SM_Env_Hell_Bone_Hand_R_01")

# the ring
LAVA_TILE = "SM_Env_Lava_Plane_01"
LAVA_TILE_SCALE = 6.0          # 197 u x 6 = 1181 u per tile
LAVA_EXTENT = 5600.0           # tiles cover +-this around the centre (the far cliffs and spikes hide the edge)
LAVA_LIT_RADIUS = 2400.0       # tiles whose centre is nearer than this get a lava light + embers, on a checkerboard
LAVA_DROP = 40.0               # lava surface this far under the platform top: a jump (56 u) gets back out


def guid(*parts):
    return str(uuid.uuid5(NS, "/".join(str(p) for p in parts)))


def yaw_quat(deg):
    h = math.radians(deg) * 0.5
    return (0.0, 0.0, math.sin(h), math.cos(h))


def bounds(mesh):
    """(min, max) per s&box axis (x, y, z) in units at scale 1: FBX x -> x, FBX z -> y, FBX y (up) -> z."""
    _, nodes = fb.parse(os.path.join(FBX_DIR, mesh + ".fbx"))
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for objs in fb.find(nodes, "Objects"):
        for o in objs[2]:
            if o[0] != "Geometry":
                continue
            for c in o[2]:
                if c[0] != "Vertices":
                    continue
                v = c[1][0]
                for axis, src in ((0, 0), (1, 2), (2, 1)):
                    vals = v[src::3]
                    lo[axis] = min(lo[axis], min(vals))
                    hi[axis] = max(hi[axis], max(vals))
    return tuple(a * IMPORT_SCALE for a in lo), tuple(a * IMPORT_SCALE for a in hi)


_BOUNDS = {}


def b(mesh):
    if mesh not in _BOUNDS:
        _BOUNDS[mesh] = bounds(mesh)
    return _BOUNDS[mesh]


def footprint(mesh, scale):
    lo, hi = b(mesh)
    return max(hi[0] - lo[0], hi[1] - lo[1]) * scale


class Stage:
    def __init__(self, seed, lookup):
        self.rng = random.Random(seed)
        self.lookup = lookup
        self.children = []
        self.n = 0

    def place(self, mesh, pos, yaw, scale, tags="", collider=True, lava=False, key=None):
        path = self.lookup[mesh.lower()]
        key = key or ("stage", self.n)
        self.n += 1
        ren = isc.renderer(path, guid(*key, "r"))
        if lava:
            ren["MaterialOverride"] = LAVA_VMAT
        comps = [ren]
        if collider:
            comps.append(isc.collider(path, guid(*key, "c")))
        s = scale if isinstance(scale, (tuple, list)) else (scale, scale, scale)
        rot = yaw if isinstance(yaw, (tuple, list)) else yaw_quat(yaw)
        self.children.append(gba.go("%s (%s)" % (mesh, ".".join(str(k) for k in key[1:])), pos, rot, s, guid(*key), comps, tags))

    def marker(self, name, pos, rot, scale=(1, 1, 1)):
        self.children.append(gba.go(name, pos, rot, scale, guid("marker", name)))


def keep_demo(o, centre):
    """Does a demo object survive the clearing? (name, position, scale) -> bool."""
    name = o["Name"].split(" (")[0]
    x, y, z = (float(v) for v in o["Position"].split(","))
    s = float(o["Scale"].split(",")[0])
    d = math.hypot(x - centre[0], y - centre[1])
    if name == CIRCLE_MESH:
        return False   # re-placed at the user's transform
    if name == "SM_Env_Lava_Cube_01" and s >= SEA_SCALE:
        return True    # the lava sea 27 m under everything
    if name in BONES and s >= COLOSSAL:
        return True    # the colossal scenery bones out in the sea
    if name in ("spawn", "boss_skull", "boss_hand_l", "boss_hand_r") or name.startswith("demo light"):
        return False
    if d < CLEAR:
        return False
    if any(name.startswith(k) for k in ALWAYS_KEEP):
        return True
    try:
        return footprint(name, s) >= BIG_FOOTPRINT
    except FileNotFoundError:
        return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--force", action="store_true", help="overwrite an existing prefab (the user's editor edits in it are LOST)")
    args = ap.parse_args()
    if os.path.exists(OUT) and not args.force:
        # the stage is hand-edited in the editor now (2026-09-22: edits baked in by tools/bake_boss_stage_patch.py);
        # regenerating it threw those edits away once already
        raise SystemExit("%s exists and carries the user's edits; pass --force only if a regenerated stage is really wanted" % OUT)
    lookup = gba.realms_lookup()
    src = json.load(open(SRC, encoding="utf-8"))
    demo = src["RootObject"]["Children"]

    cx, cy, cz = CIRCLE_POS
    lo, hi = b(CIRCLE_MESH)
    radius = (hi[0] - lo[0]) * 0.5 * CIRCLE_SCALE[0]
    top = cz + hi[2] * CIRCLE_SCALE[2]
    lava_z = top - LAVA_DROP
    print("platform radius %.0f, top z %.1f, lava z %.1f" % (radius, top, lava_z))

    st = Stage(args.seed, lookup)
    rng = st.rng

    # 1. the demo's surroundings
    kept = 0
    for o in demo:
        if keep_demo(o, (cx, cy)):
            st.children.append(o)
            kept += 1

    # 2. the platform
    st.place(CIRCLE_MESH, CIRCLE_POS, CIRCLE_ROT, CIRCLE_SCALE, key=("platform",))

    # 3. the lava ring: tiles on a grid, the inner ones lit
    tile = footprint(LAVA_TILE, 1.0) * LAVA_TILE_SCALE
    count = int(math.ceil(LAVA_EXTENT * 2 / tile))
    start = -(count - 1) * 0.5 * tile
    for i in range(count):
        for j in range(count):
            x = cx + start + i * tile
            y = cy + start + j * tile
            d = math.hypot(x - cx, y - cy)
            if d + tile * 0.7 < radius:
                continue   # fully under the platform
            lit = d < LAVA_LIT_RADIUS and (i + j) % 2 == 0
            st.place(LAVA_TILE, (x, y, lava_z), rng.choice((0, 90, 180, 270)), LAVA_TILE_SCALE, tags="lava" if lit else "lava,nolight", lava=True, key=("lava", i, j))

    def ring_pos(angle_deg, r, z):
        a = math.radians(angle_deg)
        return (cx + math.cos(a) * r, cy + math.sin(a) * r, z)

    def sunk_z(mesh, scale, sink):
        """Origin z that puts the model's bottom `sink` units under the lava surface."""
        lo_, _ = b(mesh)
        return lava_z - sink - lo_[2] * scale

    # 4. the skull's roost: a flat rock island opposite the spawn (the skull side is +x, angle 0)
    roost_scale = 2.6
    roost = ring_pos(0, radius + 560, sunk_z("SM_Env_Rock_Flat_01", roost_scale, 40))
    st.place("SM_Env_Rock_Flat_01", roost, 90, roost_scale, key=("roost",))
    roost_top = roost[2] + b("SM_Env_Rock_Flat_01")[1][2] * roost_scale
    for k, (da, dr, s) in enumerate(((150, 160, 2.2), (-140, 190, 1.8), (35, 330, 1.6))):
        p = ring_pos(0 + da * 0.1, radius + 560 + dr * 0.3, 0)
        p = (p[0] + math.cos(math.radians(da)) * dr, p[1] + math.sin(math.radians(da)) * dr, sunk_z("SM_Env_Rock_Medium_06", s, 30))
        st.place("SM_Env_Rock_Medium_06", p, rng.uniform(0, 360), s, key=("roost_rock", k))

    # 5. pillars standing in the lava: the two flanking the skull carry the hands, the rest are the arena's colonnade
    pillar_scale = 0.8
    pillar_angles = [50, -50, 110, -110, 165, -165]
    hand_tops = {}
    for k, ang in enumerate(pillar_angles):
        mesh = "SM_Env_Hell_Pillar_02" if k < 2 else rng.choice(("SM_Env_Hell_Pillar_01", "SM_Env_Hell_Pillar_04"))
        s = pillar_scale if k < 2 else rng.uniform(0.6, 0.85)
        r = radius + (380 if k < 2 else rng.uniform(420, 620))
        base_sink = 120
        z = sunk_z(mesh, s, base_sink)
        # the two hand pillars are cut down so their crowns sit just over the platform: the hands must rest in sword reach
        if k < 2:
            z = top + 70 - b(mesh)[1][2] * s
        p = ring_pos(ang, r, z)
        st.place(mesh, p, ang + 180 + rng.uniform(-15, 15), s, key=("pillar", k))
        if k < 2:
            hand_tops[ang] = (p[0], p[1], p[2] + b(mesh)[1][2] * s)

    # 6. rocks and spike clusters in the lava
    rocks = ("SM_Env_Rock_Medium_02", "SM_Env_Rock_Medium_06", "SM_Env_Hell_Rock_02", "SM_Env_Hell_Rock_03", "SM_Env_Hell_Rock_05")
    spikes = ("SM_Env_Hell_Spikes_03", "SM_Env_Hell_Spikes_04", "SM_Env_Hell_Spikes_05")
    taken = [(0, radius + 560)] + [(a, radius + 380) for a in pillar_angles[:2]]

    def free(ang, r, gap_deg=22, gap_r=350):
        for a, rr in taken:
            da = (ang - a + 180) % 360 - 180
            if abs(da) < gap_deg and abs(rr - r) < gap_r:
                return False
        return True

    placed = 0
    tries = 0
    while placed < 16 and tries < 400:
        tries += 1
        ang = rng.uniform(0, 360)
        r = radius + rng.uniform(230, 1500)
        if not free(ang, r):
            continue
        mesh = rng.choice(rocks)
        s = rng.uniform(1.6, 3.2) if "Medium" in mesh else rng.uniform(1.2, 2.2)
        st.place(mesh, ring_pos(ang, r, sunk_z(mesh, s, rng.uniform(20, 70))), rng.uniform(0, 360), s, key=("rock", placed))
        taken.append((ang, r))
        placed += 1
    placed = 0
    tries = 0
    while placed < 7 and tries < 400:
        tries += 1
        ang = rng.uniform(0, 360)
        r = radius + rng.uniform(200, 1000)
        if not free(ang, r, 18, 300):
            continue
        mesh = rng.choice(spikes)
        s = rng.uniform(1.3, 2.2)
        st.place(mesh, ring_pos(ang, r, sunk_z(mesh, s, 25)), rng.uniform(0, 360), s, key=("spikes", placed))
        taken.append((ang, r))
        placed += 1

    # 7. the rim: skull spikes every 30 degrees on the platform's edge, none where the player spawns or the skull rests
    for k in range(12):
        ang = k * 30 + 15
        if abs((ang + 180) % 360 - 180) < 25 or abs(ang - 180) < 25:
            continue
        mesh = rng.choice(("SM_Prop_Hell_Spike_Skull_01", "SM_Prop_Hell_Spike_Skull_02", "SM_Prop_Hell_Spike_Skull_03"))
        z = top - b(mesh)[0][2] - 4
        st.place(mesh, ring_pos(ang, radius - 90, z), rng.uniform(0, 360), 1.0, collider=False, key=("rim", k))

    # 7b. depth on the platform itself: six rock FORMATIONS inside the rim, a broken crown around the fight, each one
    # composed the way rock grows: a tall spire as the anchor, two smaller spires huddled at its flanks a little
    # further in, boulders at its foot toward the centre, now and then a broken pillar stub. Plus two ruined gates
    # (a pair of prop pillars) behind the spawn. The spawn side (180) and the skull side (0) stay open.
    def on_top(mesh, s, sink=0.0):
        return top - b(mesh)[0][2] * s - sink

    def near(ang, r, along, out):
        """A spot `along` units tangentially and `out` units radially from the ring point (out > 0 = toward the rim)."""
        a = math.radians(ang)
        tx, ty = -math.sin(a), math.cos(a)
        return (cx + math.cos(a) * (r + out) + tx * along, cy + math.sin(a) * (r + out) + ty * along)

    for k, ang in enumerate((70, -70, 112, -112, 150, -150)):
        r = radius - 250
        big = rng.uniform(1.15, 1.45)
        st.place("SM_Env_Hell_Rock_08", ring_pos(ang, r, on_top("SM_Env_Hell_Rock_08", big, 14)), ang + 90 + rng.uniform(-20, 20), big, key=("form", k, "anchor"))
        for j, side in enumerate((-1, 1)):
            mesh = rng.choice(("SM_Env_Hell_Rock_06", "SM_Env_Hell_Rock_07"))
            s = rng.uniform(0.85, 1.15)
            x, y = near(ang, r, side * rng.uniform(150, 210), -rng.uniform(20, 70))
            st.place(mesh, (x, y, on_top(mesh, s, 12)), ang + 90 + side * rng.uniform(20, 50), s, key=("form", k, "spire", j))
        for j in range(rng.randint(1, 2)):
            mesh = rng.choice(("SM_Env_Rock_Round_02", "SM_Env_Rock_Round_04", "SM_Env_Rock_Round_06", "SM_Env_Rock_Round_07"))
            s = rng.uniform(1.0, 1.5)
            x, y = near(ang, r, rng.uniform(-160, 160), -rng.uniform(150, 230))
            st.place(mesh, (x, y, on_top(mesh, s, 26)), rng.uniform(0, 360), s, key=("form", k, "boulder", j))
        if k % 2 == 1:
            x, y = near(ang, r, rng.choice((-1, 1)) * rng.uniform(230, 280), rng.uniform(-40, 40))
            st.place("SM_Env_Rubble_Pillar_02", (x, y, on_top("SM_Env_Rubble_Pillar_02", 1.2, 10)), rng.uniform(0, 360), 1.2, key=("form", k, "stub"))
    for k, ang in enumerate((135, -135)):
        for j, side in enumerate((-1, 1)):
            mesh = "SM_Prop_Hell_Pillar_01" if j == 0 else "SM_Prop_Hell_Pillar_02"
            x, y = near(ang, radius - 300, side * 150, 0)
            st.place(mesh, (x, y, top), ang + rng.uniform(-10, 10), 1.25, key=("gate", k, j))

    # 7c. the floating rock chains: chained boulders rising out of the lava all around the platform, the nearer ones
    # leaning in over its edge (rocks hanging over the fight), the farther ones leaning away; the models are anchored at
    # their base and lean toward their -x, so the yaw sets the lean
    chains = ("SM_Env_Hell_Floating_Rock_Chain_01", "SM_Env_Hell_Floating_Rock_Chain_02", "SM_Env_Hell_Floating_Rock_Chain_03", "SM_Env_Hell_Floating_Rock_Chain_04")
    for k, ang in enumerate((25, -25, 95, -95, 125, -125, 160, -160, 60, -60)):
        mesh = chains[k % len(chains)]
        inward = k < 6
        r = radius + (rng.uniform(220, 330) if inward else rng.uniform(700, 1100))
        s = rng.uniform(0.75, 1.0) if inward else rng.uniform(0.85, 1.15)
        yaw = (ang if inward else ang + 180) + rng.uniform(-25, 25)
        st.place(mesh, ring_pos(ang, r, sunk_z(mesh, s, 30)), yaw, s, key=("chain", k))
        taken.append((ang, r))

    # 8. markers: the player spawns on the platform's spawn side (-x) facing the skull; the skull on its roost; the hands on their pillars
    spawn = ring_pos(180, radius * 0.5, top + 8)
    st.marker("spawn", spawn, yaw_quat(0))
    st.marker("arena_center", (cx, cy, top), yaw_quat(0))
    skull_scale = 1.46991
    st.marker("boss_skull", ring_pos(0, radius + 560, roost_top + 40), yaw_quat(180), (skull_scale,) * 3)
    hand_rot = {"boss_hand_l": (0.186082106, 0.121733006, 0.91309328, -0.341782367), "boss_hand_r": (-0.121920009, 0.407043009, 0.859141017, 0.285181017)}
    hand_scale = {"boss_hand_l": 1.56354, "boss_hand_r": 1.38326}
    for name, ang in (("boss_hand_l", -50), ("boss_hand_r", 50)):
        t = hand_tops[ang]
        st.marker(name, (t[0], t[1], t[2] + 10), hand_rot[name], (hand_scale[name],) * 3)

    root = gba.go("boss_stage", (0, 0, 0), (0, 0, 0, 1), (1, 1, 1), guid("root"))
    root["Children"] = st.children
    root["__properties"] = {}
    root["__variables"] = []
    prefab = {"RootObject": root, "ResourceVersion": 2, "ShowInMenu": False, "MenuPath": None, "MenuIcon": None,
              "DontBreakAsTemplate": False, "__references": [], "__version": 2}
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(prefab, f, indent=2, ensure_ascii=False)
    print("wrote %s: %d objects (%d kept from the demo, %d new), spawn %s, skull roost top %.0f, hand rests %s" % (
        OUT, len(st.children), kept, len(st.children) - kept, tuple(round(v) for v in spawn), roost_top,
        {k: round(v[2]) for k, v in hand_tops.items()}))


if __name__ == "__main__":
    main()
