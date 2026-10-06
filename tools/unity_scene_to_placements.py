"""Turn a Unity demo scene (.unity YAML) into a placement JSON.

  python tools/unity_scene_to_placements.py <extract_dir> <source_scene_name> <out.json>

<extract_dir> is what extract_adventure.py produced: Demo.unity, guid2path.json, prefabs/<guid>.prefab.

Output is heden's placement schema (heden/Editor/SYNTY_IMPORT_FORMAT.md): raw Unity WORLD space,
metres, Y-up, left-handed, quaternions [x,y,z,w]. import_synty_scene.py does the axis/unit
conversion on the s&box side.

Two things make this more than a regex pass:
  * every PrefabInstance in these demo scenes is parented under a grouping GameObject, so local
    transforms have to be composed up the chain or a third of the scene lands on the origin
  * a prefab's name is NOT reliably its mesh name (SM_Env_Rock_10.prefab holds SM_Env_Rock_010),
    so the mesh is resolved through the prefab's own m_Mesh guid reference
"""
import json, math, os, re, sys, collections

EXTRACT, SOURCE_SCENE, OUT = sys.argv[1], sys.argv[2], sys.argv[3]

IDENT = ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0))


# ---------------------------------------------------------------- quaternion helpers
def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + y * tz - z * ty,
            vy + w * ty + z * tx - x * tz,
            vz + w * tz + x * ty - y * tx)


def compose(parent, local):
    """Unity parent-child composition: world = parent o local."""
    pp, pr, ps = parent
    lp, lr, ls = local
    sp = (lp[0] * ps[0], lp[1] * ps[1], lp[2] * ps[2])
    rp = qrot(pr, sp)
    return ((pp[0] + rp[0], pp[1] + rp[1], pp[2] + rp[2]),
            qmul(pr, lr),
            (ps[0] * ls[0], ps[1] * ls[1], ps[2] * ls[2]))


# ---------------------------------------------------------------- YAML slicing
def load_docs(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    parts = re.split(r"^--- !u!(\d+) &(\d+)(?:\s+stripped)?\s*$", text, flags=re.M)
    docs = []
    for i in range(1, len(parts), 3):
        docs.append((parts[i], parts[i + 1], parts[i + 2]))
    return docs


def f3(body, key):
    m = re.search(key + r":\s*\{x:\s*(-?[\d.eE+-]+),\s*y:\s*(-?[\d.eE+-]+),\s*z:\s*(-?[\d.eE+-]+)\}", body)
    return tuple(float(x) for x in m.groups()) if m else None


def f4(body, key):
    m = re.search(key + r":\s*\{x:\s*(-?[\d.eE+-]+),\s*y:\s*(-?[\d.eE+-]+),\s*z:\s*(-?[\d.eE+-]+),\s*w:\s*(-?[\d.eE+-]+)\}", body)
    return tuple(float(x) for x in m.groups()) if m else None


def fid(body, key):
    m = re.search(key + r":\s*\{fileID:\s*(-?\d+)", body)
    return m.group(1) if m else None


def main():
    docs = load_docs(os.path.join(EXTRACT, "Demo.unity"))
    guid2path = json.load(open(os.path.join(EXTRACT, "guid2path.json")))

    gameobjects = {}   # fileID -> name
    transforms = {}    # fileID -> dict(father, trs) for real transforms
    stripped = {}      # fileID -> prefab instance fileID
    instances = {}     # fileID -> dict(parent, guid, mods)
    lights = []

    for cls, f, body in docs:
        if cls == "1":
            m = re.search(r"m_Name:\s*(.*)", body)
            gameobjects[f] = m.group(1).strip() if m else ""
        elif cls == "4":
            pi = fid(body, "m_PrefabInstance")
            if pi and pi != "0":
                stripped[f] = pi
            else:
                transforms[f] = {
                    "father": fid(body, "m_Father"),
                    "trs": (f3(body, "m_LocalPosition") or (0, 0, 0),
                            f4(body, "m_LocalRotation") or (0, 0, 0, 1),
                            f3(body, "m_LocalScale") or (1, 1, 1)),
                }
        elif cls == "1001":
            # modifications are keyed by their prefab-internal target: an instance that also moves a CHILD of the
            # prefab (the hell skull's jaw) must not have the child's position overwrite the root's
            mods = {}
            for tgt, prop, val in re.findall(
                    r"- target: \{fileID: (-?\d+), guid: [0-9a-f]{32}, type: \d+\}\s*\n"
                    r"\s*propertyPath: (\S+)\s*\n\s*value: (\S*)", body):
                mods.setdefault(tgt, {})[prop] = val
            instances[f] = {
                "parent": fid(body, "m_TransformParent"),
                "guid": (re.search(r"m_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})", body) or [None, None])[1],
                "mods_by_target": mods,
            }
        elif cls == "108":
            lights.append((f, body))

    # ---------------------------------------------------------- prefab guid -> mesh basename
    mesh_of = {}
    multi = []
    root_xform = {}   # prefab guid -> fileID of its root transform (the one with m_Father 0)
    for guid in {v["guid"] for v in instances.values() if v["guid"]}:
        p = os.path.join(EXTRACT, "prefabs", guid + ".prefab")
        if not os.path.exists(p):
            continue
        text = open(p, encoding="utf-8", errors="replace").read()
        for f_id, tbody in re.findall(r"^--- !u!4 &(\d+)\s*$\n(.*?)(?=^--- !u!|\Z)", text, flags=re.M | re.S):
            if re.search(r"m_Father:\s*\{fileID:\s*0\}", tbody):
                root_xform[guid] = f_id
                break
        found = []
        for mg in re.findall(r"m_Mesh:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32})", text):
            path = guid2path.get(mg)
            if path and path.lower().endswith(".fbx"):
                stem = os.path.basename(path)[:-4]
                if stem not in found:
                    found.append(stem)
        if len(found) == 1:
            mesh_of[guid] = found[0]
        elif len(found) > 1:
            mesh_of[guid] = found[0]
            multi.append((guid2path.get(guid, guid), found))

    # ---------------------------------------------------------- world transforms
    cache = {}

    def inst_trs(f):
        """Local TRS of a prefab instance, from its modification list."""
        inst = instances[f]
        by_target = inst["mods_by_target"]
        root = root_xform.get(inst["guid"])
        # the root transform's own modifications; if the prefab's root is unknown, the union (the old behaviour)
        mods = by_target[root] if root in by_target else {k: v for m in by_target.values() for k, v in m.items()}
        def g(k, d):
            v = mods.get(k)
            return float(v) if v not in (None, "") else d
        return ((g("m_LocalPosition.x", 0.0), g("m_LocalPosition.y", 0.0), g("m_LocalPosition.z", 0.0)),
                (g("m_LocalRotation.x", 0.0), g("m_LocalRotation.y", 0.0),
                 g("m_LocalRotation.z", 0.0), g("m_LocalRotation.w", 1.0)),
                (g("m_LocalScale.x", 1.0), g("m_LocalScale.y", 1.0), g("m_LocalScale.z", 1.0)))

    def world_of_transform(f, depth=0):
        """World TRS of a transform fileID (real or stripped)."""
        if f in (None, "0"):
            return IDENT
        key = ("t", f)
        if key in cache:
            return cache[key]
        if depth > 32:
            raise RuntimeError("transform parent cycle at %s" % f)
        if f in stripped:
            res = world_of_instance(stripped[f], depth + 1)
        elif f in transforms:
            t = transforms[f]
            res = compose(world_of_transform(t["father"], depth + 1), t["trs"])
        else:
            res = IDENT
        cache[key] = res
        return res

    def world_of_instance(f, depth=0):
        key = ("i", f)
        if key in cache:
            return cache[key]
        if depth > 32:
            raise RuntimeError("instance parent cycle at %s" % f)
        res = compose(world_of_transform(instances[f]["parent"], depth + 1), inst_trs(f))
        cache[key] = res
        return res

    # ---------------------------------------------------------- emit
    objects = []
    skipped = collections.Counter()
    for f in sorted(instances, key=lambda x: int(x)):
        v = instances[f]
        if not v["guid"]:
            skipped["no source prefab"] += 1
            continue
        mesh = mesh_of.get(v["guid"])
        if not mesh:
            skipped[os.path.basename(guid2path.get(v["guid"], v["guid"]))] += 1
            continue
        pos, rot, scale = world_of_instance(f)
        objects.append({
            "mesh": mesh,
            "pos": [round(x, 5) for x in pos],
            "rot": [round(x, 6) for x in rot],
            "scale": [round(x, 5) for x in scale],
        })

    light_out = []
    # map GameObject fileID -> transform fileID (real transforms only)
    go2xform = {}
    for cls, f, body in docs:
        if cls == "4" and f in transforms:
            g = fid(body, "m_GameObject")
            if g:
                go2xform[g] = f
    TYPES = {0: "Spot", 1: "Directional", 2: "Point", 3: "Rectangle", 4: "Disc"}
    for f, body in lights:
        go = fid(body, "m_GameObject")
        tf = go2xform.get(go)
        pos, rot, _ = world_of_transform(tf) if tf else IDENT
        col = re.search(r"m_Color:\s*\{r:\s*(-?[\d.eE+-]+),\s*g:\s*(-?[\d.eE+-]+),\s*b:\s*(-?[\d.eE+-]+)", body)
        def num(k, d=0.0):
            m = re.search(k + r":\s*(-?[\d.eE+-]+)", body)
            return float(m.group(1)) if m else d
        light_out.append({
            "type": TYPES.get(int(num("m_Type", 1)), "Point"),
            "mode": "Realtime",
            "name": gameobjects.get(go, "Light"),
            "pos": [round(x, 5) for x in pos],
            "rot": [round(x, 6) for x in rot],
            "color": [float(col.group(1)), float(col.group(2)), float(col.group(3))] if col else [1, 1, 1],
            "intensity": num("m_Intensity", 1.0),
            "range": num("m_Range", 10.0),
            "spot_angle": num("m_SpotAngle", 60.0),
            "shadows": True,
        })

    doc = {
        "source_scene": SOURCE_SCENE,
        "units": "meters",
        "note": "generated by tools/unity_scene_to_placements.py from the Synty demo scene",
        "count": len(objects),
        "objects": objects,
        "light_count": len(light_out),
        "lights": light_out,
    }
    os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
    json.dump(doc, open(OUT, "w"), indent=1)

    print("prefab instances : %d" % len(instances))
    print("emitted objects  : %d across %d meshes" % (len(objects), len({o["mesh"] for o in objects})))
    print("lights           : %d %s" % (len(light_out), [l["type"] for l in light_out]))
    print("group transforms : %d" % len(transforms))
    if multi:
        print("multi-mesh prefabs (first mesh used):")
        for p, f in multi[:20]:
            print("   %-55s %s" % (p, f))
    if skipped:
        print("SKIPPED:")
        for k, c in skipped.most_common():
            print("   %-45s %d" % (k, c))
    # sanity: bounding box
    if objects:
        for i, ax in enumerate("xyz"):
            lo = min(o["pos"][i] for o in objects)
            hi = max(o["pos"][i] for o in objects)
            print("extent %s: %9.2f .. %9.2f m" % (ax, lo, hi))


if __name__ == "__main__":
    main()
