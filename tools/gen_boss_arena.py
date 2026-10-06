"""Write Assets/Prefabs/hell_arena.prefab: the Bone Colossus arena, the Dungeon Realms Demo_Hell_01 scene 1:1.

    python tools/gen_boss_arena.py <unity_export.json> --roots <extract_dir>/placements.json

<unity_export.json> is the user's own Unity exporter's output (heden's placement schema: raw Unity world space,
metres, Y-up, quats [x,y,z,w]; "C:/Users/skipp/My project (5)/Demo_Hell_01_placements.json"): Unity computed every
world transform itself, so it is the truth for the 300 mesh objects and the demo's lights. --roots is
tools/unity_scene_to_placements.py's output for the same scene, used for two things Unity's export cannot give:
  * the particle FX_ prefabs (absent from the export): four of them emit real rock meshes, stood in for by a
    seeded scatter of that mesh around the emitter (FX_ROCKS)
  * the prefab ROOT transforms of the bone pieces: the skull prefab is two meshes (skull + jaw sub-mesh) and the
    export lists both under the skull's name, while the boss needs the root (the jaw is the boss's own child)
Every mesh becomes a static child (ModelRenderer + ModelCollider, import_synty_scene.py's Unity -> s&box maths),
the Dungeon Realms vmdl preferred over other packs' models of the same name. Lava renders on
shaders/realmslava.vmat. The demo's point lights come along (shadowless, intensities folded into the colour).

The prefab is meant to be EDITED in the s&box editor afterwards; GUIDs are stable across re-runs. Four marker
children carry what the code reads at runtime (Code/Boss/HellArena.cs): `spawn` (the Hell ritual circle) and
`boss_skull` / `boss_hand_l` / `boss_hand_r` = the demo's SMALL bone set (the 1.47x skull on the rocks, the
1.4x / 1.6x hands lying on the pillars by the circle), transform and scale straight from the demo: the dormant
boss. The colossal 15.6x skull and 12x hands out in the lava sea stay as scenery.
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
import import_synty_scene as isc  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "Prefabs", "hell_arena.prefab")
NS = uuid.UUID("2b7d6d3e-5c1a-4f0e-9c2f-7a8e1b6d4c11")
LAVA_VMAT = "shaders/realmslava.vmat"
SKIP = ("FX_", "SM_SkyDome")
BONES = {"SM_Env_Hell_Bone_Skull_01": "boss_skull", "SM_Env_Hell_Bone_Hand_L_01": "boss_hand_l", "SM_Env_Hell_Bone_Hand_R_01": "boss_hand_r"}
COLOSSAL = 5.0
SPAWN = ((2002.0, 59.0, 170.0), (0.0, 0.0, 0.0, 1.0))
# The --roots file resolves a particle prefab to the mesh it emits, so an emitter shows up there as a plain rock
# with no counterpart in the Unity export (which has no particle systems): (count, radius m, height m, scale range)
# of the stand-in scatter per emitted mesh. The lava rock emitters (still / large / spread) all emit the small rock.
FX_ROCKS = {
    "SM_Prop_Rock_Small_01": (6, 4.5, 0.0, (0.7, 2.0)),
    "SM_Env_Hell_Rock_01": (5, 9.0, 8.0, (0.7, 1.2)),
}


def guid(*parts):
    return str(uuid.uuid5(NS, "/".join(str(p) for p in parts)))


def go(name, pos, rot, scale, gid, components=(), tags=""):
    return {
        "__guid": gid, "__version": 2, "Flags": 0, "Name": name,
        "Position": isc.v3(pos), "Rotation": isc.v4(rot), "Scale": isc.v3(scale), "Tags": tags, "Enabled": True,
        "NetworkMode": 2, "NetworkFlags": 0, "NetworkOrphaned": 0, "NetworkTransmit": True, "OwnerTransfer": 1,
        "Components": list(components), "Children": [],
    }


def realms_lookup():
    """Every vmdl basename -> engine path, the Dungeon Realms copy first (the shared lookup takes the alphabetically
    first pack, which handed 26 cliffs, stairs and a flat rock to Dark Fantasy's and DungeonPack's models)."""
    lookup = isc.build_model_lookup()
    realms = os.path.join(ROOT, "Assets", "DungeonRealms")
    for f in os.listdir(realms):
        if f.lower().endswith(".vmdl"):
            lookup[f[:-5].lower()] = "dungeonrealms/" + f.lower()
    return lookup


def static(key, mesh, path, pos, rot, scale):
    p, q, s = isc.convert(pos, rot, scale)
    ren = isc.renderer(path, guid(*key, "r"))
    if "Lava" in mesh:
        ren["MaterialOverride"] = LAVA_VMAT
    return go("%s (%s)" % (mesh, ".".join(str(k) for k in key[1:])), p, q, s, guid(*key), [ren, isc.collider(path, guid(*key, "c"))])


def fx_rocks(i, o, lookup):
    count, radius, height, (s0, s1) = FX_ROCKS[o["mesh"]]
    mesh = o["mesh"]
    path = lookup[mesh.lower()]
    rng = random.Random(i * 7919)
    out = []
    for k in range(count):
        a = rng.uniform(0, math.tau)
        r = radius * math.sqrt(rng.random())
        h = rng.uniform(0.3, 1.0) * height
        pos = [o["pos"][0] + math.cos(a) * r, o["pos"][1] + h, o["pos"][2] + math.sin(a) * r]
        yaw = rng.uniform(0, math.tau)
        sc = rng.uniform(s0, s1)
        out.append(static(("fx", i, k), o["mesh"], path, pos, [0.0, math.sin(yaw / 2), 0.0, math.cos(yaw / 2)], [sc, sc, sc]))
    return out


def demo_light(i, l):
    """A demo point light: URP intensities run 10..2000, folded into the colour as a 0.3..3 multiplier."""
    pos, rot, _ = isc.convert(l["pos"], l.get("rot", [0, 0, 0, 1]), [1, 1, 1])
    k = min(3.0, max(0.3, l.get("intensity", 1.0) / 100.0))
    col = [min(3.0, c * k) for c in l.get("color", [1, 1, 1])]
    light = isc.point_light(guid("arena", "light", i, "c"), col, l.get("range", 10.0) * isc.METERS_TO_INCHES)
    light["Attenuation"] = 1.0
    return go("demo light (%d)" % i, pos, rot, [1, 1, 1], guid("arena", "light", i), [light])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export", help="the Unity exporter's placement JSON (world transforms from Unity itself)")
    ap.add_argument("--roots", required=True, help="unity_scene_to_placements.py output: FX emitters and the bone prefab roots")
    ap.add_argument("--no-lights", action="store_true")
    args = ap.parse_args()
    doc = json.load(open(args.export, encoding="utf-8"))
    roots = json.load(open(args.roots, encoding="utf-8"))
    lookup = realms_lookup()

    children, missing = [], {}
    kept = dropped = 0
    for i, o in enumerate(doc["objects"]):
        mesh = o["mesh"]
        if mesh in BONES or any(mesh.startswith(s) for s in SKIP):
            dropped += 1   # bones come from --roots below (root transforms), FX are particles
            continue
        path = lookup.get(mesh.lower())
        if not path:
            missing[mesh] = missing.get(mesh, 0) + 1
            continue
        children.append(static(("arena", i), mesh, path, o["pos"], o["rot"], o.get("scale", [1, 1, 1])))
        kept += 1

    markers = {}
    fx = 0
    exported = {}
    for o in doc["objects"]:
        exported.setdefault(o["mesh"], []).append(o["pos"])
    for i, o in enumerate(roots["objects"]):
        mesh = o["mesh"]
        if mesh in FX_ROCKS:
            if any(math.dist(p, o["pos"]) < 0.05 for p in exported.get(mesh, [])):
                continue   # a real placed rock, already in from the export
            rocks = fx_rocks(i, o, lookup)
            children.extend(rocks)
            fx += len(rocks)
        elif mesh in BONES:
            colossal = max(o.get("scale", [1, 1, 1])) > COLOSSAL
            if colossal:
                children.append(static(("arena", "root", i), mesh, lookup[mesh.lower()], o["pos"], o["rot"], o["scale"]))
                kept += 1
            else:
                name = BONES[mesh]
                if name in markers:
                    raise SystemExit("two small %s placements" % mesh)
                markers[name] = isc.convert(o["pos"], o["rot"], o.get("scale", [1, 1, 1]))
    if set(markers) != set(BONES.values()):
        raise SystemExit("small bone placements missing: have %s" % sorted(markers))

    lights = 0
    if not args.no_lights:
        for i, l in enumerate(doc.get("lights", [])):
            if l.get("type") == "Point":
                children.append(demo_light(i, l))
                lights += 1

    children.append(go("spawn", SPAWN[0], SPAWN[1], (1, 1, 1), guid("arena", "marker", "spawn")))
    for name, (pos, rot, scale) in markers.items():
        children.append(go(name, pos, rot, scale, guid("arena", "marker", name)))
        print("  marker %-12s pos (%7.0f %7.0f %7.0f) scale %.2f" % (name, pos[0], pos[1], pos[2], scale[0]))

    root = go("hell_arena", (0, 0, 0), (0, 0, 0, 1), (1, 1, 1), guid("arena", "root"))
    root["Children"] = children
    root["__properties"] = {}
    root["__variables"] = []
    prefab = {"RootObject": root, "ResourceVersion": 2, "ShowInMenu": False, "MenuPath": None, "MenuIcon": None,
              "DontBreakAsTemplate": False, "__references": [], "__version": 2}
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(prefab, f, indent=2, ensure_ascii=False)
    print("wrote %s: %d statics, %d scattered rocks, %d demo lights, %d dropped, %d markers" % (OUT, kept, fx, lights, dropped, len(markers) + 1))
    if missing:
        print("MISSING vmdls:", missing)


if __name__ == "__main__":
    main()
