"""Copy the toonified Fantasy Kingdom pack + the houses scene from heden into voidmage.

heden already did the toonify and the Unity->s&box scene conversion for
Demo_Layout_Houses_With_Interiors.unity, and the result uses only engine-native components
(ModelRenderer / ModelCollider / CameraComponent / DirectionalLight), so this is a pure copy.

  python tools/copy_fantasykingdom.py [--check]

--check reports what would be copied without writing anything.

Deliberately NOT copied:
  *.vmdl_c   compiled output (51 MB); s&box recompiles on first open
  *.vtex_c   ditto for textures
  *.scene_c / *.scene_d
"""
import os, re, shutil, sys, filecmp

HEDEN = r"c:\Users\skipp\Documents\s&box projects\heden"
VOID = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
CHECK = "--check" in sys.argv

SCENE_SRC = os.path.join(HEDEN, "Assets", "scenes", "castlehousepresets.scene")
SCENE_DST = os.path.join(VOID, "Assets", "scenes", "fantasykingdom_houses.scene")


def copy(src, dst, stats):
    if os.path.exists(dst) and os.path.getsize(dst) == os.path.getsize(src) and filecmp.cmp(src, dst, shallow=False):
        stats["same"] += 1
        return
    stats["copied"] += 1
    stats["bytes"] += os.path.getsize(src)
    if not CHECK:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(src, dst)


def run():
    total = {"copied": 0, "same": 0, "bytes": 0}

    # 1. models -------------------------------------------------------------
    src = os.path.join(HEDEN, "Assets", "FantasyKingdom")
    dst = os.path.join(VOID, "Assets", "FantasyKingdom")
    s = {"copied": 0, "same": 0, "bytes": 0}
    for f in sorted(os.listdir(src)):
        ext = os.path.splitext(f)[1].lower()
        if ext in (".fbx", ".vmdl") or f.endswith("_placements.json"):
            copy(os.path.join(src, f), os.path.join(dst, f), s)
    print("models   : %4d copied, %4d already identical, %.1f MB" % (s["copied"], s["same"], s["bytes"] / 1048576))
    for k in total: total[k] += s[k]

    # 2. textures -----------------------------------------------------------
    src = os.path.join(HEDEN, "Assets", "materials", "FantasyKingdomTextures")
    dst = os.path.join(VOID, "Assets", "materials", "FantasyKingdomTextures")
    s = {"copied": 0, "same": 0, "bytes": 0}
    for f in sorted(os.listdir(src)):
        if os.path.splitext(f)[1].lower() in (".png", ".tga", ".jpg"):
            copy(os.path.join(src, f), os.path.join(dst, f), s)
    # the wheat vmat reaches outside the pack folder for this one
    copy(os.path.join(HEDEN, "Assets", "materials", "heden_flat_wheat.png"),
         os.path.join(VOID, "Assets", "materials", "heden_flat_wheat.png"), s)
    print("textures : %4d copied, %4d already identical, %.1f MB" % (s["copied"], s["same"], s["bytes"] / 1048576))
    for k in total: total[k] += s[k]

    # 3. materials ----------------------------------------------------------
    src = os.path.join(HEDEN, "Assets", "Shaders")
    dst = os.path.join(VOID, "Assets", "Shaders")
    s = {"copied": 0, "same": 0, "bytes": 0}
    for f in sorted(os.listdir(src)):
        if f.lower().startswith("fantasykingdom") and f.lower().endswith(".vmat"):
            copy(os.path.join(src, f), os.path.join(dst, f), s)
    print("vmats    : %4d copied, %4d already identical" % (s["copied"], s["same"]))
    for k in total: total[k] += s[k]

    # 4. scene --------------------------------------------------------------
    # The GameObject tree is heden's import output and stays verbatim. Two things change:
    # the Title, and any GameObjectSystem holding a $blob -- those blobs live in the
    # castlehousepresets.scene_d sidecar (48 bytes, an empty clutter grid) which is not worth
    # copying, and a dangling blob guid is worse than an absent system.
    import json as _json
    scene = _json.load(open(SCENE_SRC, encoding="utf-8"))
    scene["Title"] = "Fantasy Kingdom Houses"
    dropped = []
    sysd = scene.get("SceneProperties", {}).get("GameObjectSystems")
    if isinstance(sysd, dict):
        for k in [k for k, v in sysd.items() if "$blob" in _json.dumps(v)]:
            del sysd[k]
            dropped.append(k)
    if not CHECK:
        os.makedirs(os.path.dirname(SCENE_DST), exist_ok=True)
        with open(SCENE_DST, "w", encoding="utf-8", newline="\n") as fh:
            _json.dump(scene, fh, indent=2)
    print("scene    : %s -> %s%s" % (os.path.basename(SCENE_SRC), os.path.basename(SCENE_DST),
                                     ("  (dropped blob systems: %s)" % ", ".join(dropped)) if dropped else ""))

    print("\nTOTAL    : %d files, %.1f MB%s" % (total["copied"], total["bytes"] / 1048576,
                                                 "  (--check, nothing written)" if CHECK else ""))


if __name__ == "__main__":
    run()
