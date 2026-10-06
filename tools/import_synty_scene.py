"""Turn a Synty placement JSON into an s&box .scene.

  python tools/import_synty_scene.py <placements.json> <out.scene> [--template <scene>]
                                     [--no-collider <regex>] [--title <str>]

This is a Python port of the conversion in heden/Editor/SyntySceneImport.cs. Only the maths is
ported -- that file is an editor window wired into Heden.Client / ScenePerfRules / InstancedProps,
none of which exist here, and voidmage has no editor assembly to hang a menu off.

The output is byte-for-byte the same shape as heden's importer produced in
castlehousepresets.scene (which is why that scene is the default template): a root GameObject
named after source_scene, one child per placement named "<mesh> (<index>)", a ModelRenderer on
every one and a ModelCollider on the ones that should block.

Model paths are resolved by indexing every .vmdl basename under Assets/, case-insensitively,
so a placement's "mesh" does not need to know which pack folder it ended up in.

GUIDs are derived deterministically from the source scene + index, so re-running this produces
an identical file and the scene diffs cleanly instead of churning every guid.
"""
import json, math, os, re, sys, uuid

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
NS = uuid.UUID("6f1a3c4e-8b2d-4f77-9a51-2c0d6e8b4a19")

METERS_TO_INCHES = 39.3701

# Unity -> s&box change of basis. Row i is the s&box-space image of Unity basis axis i:
#   Unity +X -> s&box -Y ; +Y -> +Z ; +Z -> +X.   det = -1 (handedness flip).
# Proven by heden's FantasyKingdom import -- edit ONLY this if a future pack disagrees.
C = ((0.0, -1.0, 0.0),
     (0.0, 0.0, 1.0),
     (1.0, 0.0, 0.0))
CT = tuple(zip(*C))   # orthogonal signed permutation, so inverse == transpose


def mat_id():
    return [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def m3_to_m4(m):
    r = mat_id()
    for i in range(3):
        for j in range(3):
            r[i][j] = m[i][j]
    return r


def from_quat(q):
    """System.Numerics.CreateFromQuaternion, row-vector convention."""
    x, y, z, w = q
    m = mat_id()
    m[0][0] = 1 - 2 * (y * y + z * z); m[0][1] = 2 * (x * y + w * z); m[0][2] = 2 * (x * z - w * y)
    m[1][0] = 2 * (x * y - w * z); m[1][1] = 1 - 2 * (x * x + z * z); m[1][2] = 2 * (y * z + w * x)
    m[2][0] = 2 * (x * z + w * y); m[2][1] = 2 * (y * z - w * x); m[2][2] = 1 - 2 * (x * x + y * y)
    return m


def to_quat(m):
    """Inverse of from_quat. m must be a pure rotation (rows already normalised)."""
    t = m[0][0] + m[1][1] + m[2][2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        return ((m[1][2] - m[2][1]) / s, (m[2][0] - m[0][2]) / s, (m[0][1] - m[1][0]) / s, 0.25 * s)
    if m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = math.sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2
        return (0.25 * s, (m[0][1] + m[1][0]) / s, (m[0][2] + m[2][0]) / s, (m[1][2] - m[2][1]) / s)
    if m[1][1] > m[2][2]:
        s = math.sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2
        return ((m[0][1] + m[1][0]) / s, 0.25 * s, (m[1][2] + m[2][1]) / s, (m[2][0] - m[0][2]) / s)
    s = math.sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2
    return ((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s, 0.25 * s, (m[0][1] - m[1][0]) / s)


def convert(pos, rot, scale):
    """Unity world TRS -> s&box world TRS."""
    # Mirrored placements (negative scale) are un-mirrored HERE, on the source values, not after
    # decomposition. Decompose is free to move the reflection onto a different axis than the
    # source's and compensate with a hidden 180 deg rotation -- abs() after the fact keeps that
    # bogus rotation and the piece faces the wrong way. The only loss is the mirror itself.
    s = [abs(v) for v in scale]
    m = mat_id()
    for i in range(3):
        m[i][i] = s[i]
    m = mat_mul(m, from_quat(rot))
    m[3][0], m[3][1], m[3][2] = pos

    m = mat_mul(mat_mul(m3_to_m4(CT), m), m3_to_m4(C))

    t = (m[3][0], m[3][1], m[3][2])
    rows = [m[i][:3] for i in range(3)]
    sc = [math.sqrt(sum(v * v for v in r)) for r in rows]
    rn = [[v / sc[i] if sc[i] > 1e-12 else 0.0 for v in rows[i]] for i in range(3)]
    q = to_quat(rn)
    n = math.sqrt(sum(v * v for v in q)) or 1.0
    q = tuple(v / n for v in q)
    # Never emit a negative scale: it renders inside-out and reads black in the toon shader.
    return ([v * METERS_TO_INCHES for v in t], q, [abs(v) for v in sc])


# ---------------------------------------------------------------- scene assembly
def guid(*parts):
    return str(uuid.uuid5(NS, "/".join(str(p) for p in parts)))


def v3(v):
    return "%.6g,%.6g,%.6g" % tuple(v)


def v4(v):
    return "%.9g,%.9g,%.9g,%.9g" % tuple(v)


def build_model_lookup():
    """Every .vmdl basename under Assets/ -> engine-relative path, case-insensitive.
    Deterministic ordinal tie-break so cross-pack duplicates always resolve the same way."""
    assets = os.path.join(PROJ, "Assets")
    found = {}
    for root, _dirs, files in os.walk(assets):
        for f in files:
            if not f.lower().endswith(".vmdl"):
                continue
            rel = os.path.relpath(os.path.join(root, f), assets).replace(os.sep, "/").lower()
            key = f[:-5].lower()
            if key not in found or rel < found[key]:
                found[key] = rel
    return found


def go(name, pos, rot, scale, gid, components=None, children=None):
    return {
        "__guid": gid,
        "__version": 2,
        "Flags": 0,
        "Name": name,
        "Position": v3(pos),
        "Rotation": v4(rot),
        "Scale": v3(scale),
        "Tags": "",
        "Enabled": True,
        "NetworkMode": 2,
        "NetworkFlags": 0,
        "NetworkOrphaned": 0,
        "NetworkTransmit": True,
        "OwnerTransfer": 1,
        "Components": components or [],
        "Children": children or [],
    }


def renderer(model, gid):
    return {
        "__type": "Sandbox.ModelRenderer",
        "__guid": gid,
        "__enabled": True,
        "Flags": 0,
        "BodyGroups": 18446744073709551615,
        "CreateAttachments": False,
        "LodOverride": None,
        "MaterialGroup": None,
        "MaterialOverride": None,
        "Materials": None,
        "Model": model,
        "OnComponentDestroy": None,
        "OnComponentDisabled": None,
        "OnComponentEnabled": None,
        "OnComponentFixedUpdate": None,
        "OnComponentStart": None,
        "OnComponentUpdate": None,
        "RenderOptions": {"GameLayer": True, "OverlayLayer": False, "BloomLayer": False, "AfterUILayer": False},
        "RenderType": "On",
        "Tint": "1,1,1,1",
    }


def collider(model, gid):
    return {
        "__type": "Sandbox.ModelCollider",
        "__guid": gid,
        "__enabled": True,
        "Flags": 0,
        "ColliderFlags": 0,
        "Elasticity": None,
        "Friction": None,
        "IsTrigger": False,
        "Model": model,
        "OnComponentDestroy": None,
        "OnComponentDisabled": None,
        "OnComponentEnabled": None,
        "OnComponentFixedUpdate": None,
        "OnComponentStart": None,
        "OnComponentUpdate": None,
        "OnObjectTriggerEnter": None,
        "OnObjectTriggerExit": None,
        "OnTriggerEnter": None,
        "OnTriggerExit": None,
        "RollingResistance": None,
        "Static": True,
        "Surface": None,
        "SurfaceVelocity": "0,0,0",
    }


def point_light(gid, color, radius):
    return {
        "__type": "Sandbox.PointLight",
        "__guid": gid,
        "__enabled": True,
        "Flags": 0,
        "Attenuation": 0,
        "Contribution": "Diffuse, Specular, Transmissive",
        "FogMode": "Enabled",
        "FogStrength": 1,
        "LightColor": "%.5g,%.5g,%.5g,1" % tuple(color),
        "OnComponentDestroy": None,
        "OnComponentDisabled": None,
        "OnComponentEnabled": None,
        "OnComponentFixedUpdate": None,
        "OnComponentStart": None,
        "OnComponentUpdate": None,
        "Radius": radius,
        "ShadowBias": 0,
        "ShadowHardness": 0,
        "Shadows": False,
    }


def strip_blobs(props):
    """Drop GameObjectSystems entries that reference a $blob living in a .scene_d sidecar we
    are not carrying over -- a dangling blob guid is worse than an absent system."""
    props = json.loads(json.dumps(props))
    sysd = props.get("GameObjectSystems")
    if isinstance(sysd, dict):
        for k in [k for k, v in sysd.items() if "$blob" in json.dumps(v)]:
            del sysd[k]
    return props


def main():
    args = sys.argv[1:]
    src, out = args[0], args[1]

    def opt(flag, default=None):
        return args[args.index(flag) + 1] if flag in args else default

    template = opt("--template", os.path.join(PROJ, "Assets", "scenes", "fantasykingdom_houses.scene"))
    no_collider = opt("--no-collider")
    title = opt("--title")
    rx = re.compile(no_collider, re.I) if no_collider else None

    doc = json.load(open(src, encoding="utf-8"))
    lookup = build_model_lookup()
    tmpl = json.load(open(template, encoding="utf-8"))

    root_name = doc.get("source_scene", "import")
    children = []
    missing = {}
    ncol = 0
    for i, o in enumerate(doc["objects"]):
        key = o["mesh"].lower()
        path = lookup.get(key)
        if not path:
            missing[o["mesh"]] = missing.get(o["mesh"], 0) + 1
            continue
        pos, rot, scale = convert(o["pos"], o["rot"], o.get("scale", [1, 1, 1]))
        comps = [renderer(path, guid(root_name, i, "r"))]
        if not (rx and rx.search(o["mesh"])):
            comps.append(collider(path, guid(root_name, i, "c")))
            ncol += 1
        children.append(go("%s (%d)" % (o["mesh"], i), pos, rot, scale, guid(root_name, i), comps))

    # lights: Directional is skipped (the scene keeps its own sun), as in heden's importer
    lights = []
    skipped_lights = 0
    for i, l in enumerate(doc.get("lights", [])):
        if l.get("type") != "Point":
            skipped_lights += 1
            continue
        pos, rot, _ = convert(l["pos"], l.get("rot", [0, 0, 0, 1]), [1, 1, 1])
        inten = l.get("intensity", 1.0)
        col = [min(1.0, c * inten) for c in l.get("color", [1, 1, 1])]
        lights.append(go(l.get("name", "Point Light"), pos, rot, [1, 1, 1], guid(root_name, "light", i),
                         [point_light(guid(root_name, "light", i, "c"), col,
                                      l.get("range", 10.0) * METERS_TO_INCHES)]))
    if lights:
        children.append(go("Lights", [0, 0, 0], [0, 0, 0, 1], [1, 1, 1], guid(root_name, "lights"), [], lights))

    roots = [g for g in tmpl["GameObjects"] if g["Name"] in ("Main Camera", "Directional Light")]
    roots = json.loads(json.dumps(roots))
    for r in roots:
        r["__guid"] = guid(root_name, r["Name"])
        for c in r["Components"]:
            c["__guid"] = guid(root_name, r["Name"], c["__type"])
    roots.append(go(root_name, [0, 0, 0], [0, 0, 0, 1], [1, 1, 1], guid(root_name, "root"), [], children))

    scene = {
        "__guid": guid(root_name, "scene"),
        "GameObjects": roots,
        "SceneProperties": strip_blobs(tmpl["SceneProperties"]),
        "ResourceVersion": tmpl.get("ResourceVersion", 3),
        "Title": title or root_name,
        "Description": doc.get("note"),
        "__references": [],
        "__version": tmpl.get("__version", 3),
    }
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(scene, fh, indent=2)

    print("placements  : %d" % len(doc["objects"]))
    print("written     : %d objects (%d with colliders, %d renderer-only)"
          % (len(children) - (1 if lights else 0), ncol, len(children) - (1 if lights else 0) - ncol))
    print("lights      : %d point, %d skipped (directional/other)" % (len(lights), skipped_lights))
    print("scene       : %s (%.1f MB)" % (out, os.path.getsize(out) / 1048576))
    if missing:
        print("MISSING MODELS (no .vmdl found under Assets/):")
        for k, v in sorted(missing.items(), key=lambda x: -x[1]):
            print("   %-45s %d" % (k, v))


if __name__ == "__main__":
    main()
