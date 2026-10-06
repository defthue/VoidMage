"""Bake the user's stage edits from the boss test scene into the stage prefab.

    python tools/bake_boss_stage_patch.py [--scene Assets/scenes/boss_test.scene] [--dry-run]

The stage is edited in the editor as a prefab INSTANCE inside scenes/boss_test.scene: the editor stores those
edits as the instance's `__PrefabInstancePatch` (objects removed, properties overridden, children reordered), so
the test scene shows them but the run (HellArena.Build clones `boss_arena_prefab`) never does. This applies the
patch to the prefab itself, writes it, and then rewrites the scene's instance with a clean patch (the root's name
and transform only) and an id map pruned to the objects left, so the two agree. Run it after every stage edit
that should reach the run. The generator tools/gen_boss_stage.py refuses to overwrite the prefab from here on.

Reload the scene in the editor after running this (the editor's copy still holds the old patch; saving it again
is harmless, the patch would only re-remove what is already gone, but a fresh load is cleaner).
"""
import argparse
import json
import os

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)


def index(root):
    """Every object and component of a prefab tree by guid, with the object it lives in."""
    objs, comps = {}, {}

    def walk(o, parent):
        objs[o["__guid"]] = (o, parent)
        for c in o.get("Components", []):
            comps[c["__guid"]] = (c, o)
        for ch in o.get("Children", []):
            walk(ch, o)

    walk(root, None)
    return objs, comps


def apply_patch(root, patch, log):
    objs, comps = index(root)
    removed_objs = removed_comps = 0
    for r in patch.get("RemovedObjects", []):
        t, i = r["Id"]["Type"], r["Id"]["IdValue"]
        if t == "GameObject" and i in objs:
            o, parent = objs[i]
            if parent is None:
                log.append("cannot remove the root")
                continue
            parent["Children"] = [c for c in parent["Children"] if c["__guid"] != i]
            removed_objs += 1
        elif t == "Component" and i in comps:
            c, owner = comps[i]
            if owner.get("Components") and any(x["__guid"] == i for x in owner["Components"]):
                owner["Components"] = [x for x in owner["Components"] if x["__guid"] != i]
                removed_comps += 1
        else:
            log.append("removed id not in the prefab (already gone?): %s %s" % (t, i))
    objs, comps = index(root)
    overrides = 0
    for po in patch.get("PropertyOverrides", []):
        t, i, prop, value = po["Target"]["Type"], po["Target"]["IdValue"], po["Property"], po["Value"]
        if t == "GameObject" and i in objs:
            o = objs[i][0]
            if o is root and prop == "Name":
                continue  # the instance's name (hell_arena) is the scene's business, the prefab root keeps its own
            o[prop] = value
            overrides += 1
        elif t == "Component" and i in comps:
            comps[i][0][prop] = value
            overrides += 1
        else:
            log.append("override target not in the prefab: %s %s.%s" % (t, i, prop))
    moved = 0
    for m in patch.get("MovedObjects", []):
        i = m["Id"]["IdValue"]
        np_id = m["NewParent"]["IdValue"]
        prev = (m.get("NewPreviousElement") or {}).get("IdValue")
        if i not in objs or np_id not in objs:
            log.append("moved object or its parent not in the prefab: %s -> %s" % (i, np_id))
            continue
        o, old_parent = objs[i]
        old_parent["Children"] = [c for c in old_parent["Children"] if c["__guid"] != i]
        new_parent = objs[np_id][0]
        kids = new_parent.setdefault("Children", [])
        at = 0
        if prev:
            for n, c in enumerate(kids):
                if c["__guid"] == prev:
                    at = n + 1
                    break
        kids.insert(at, o)
        objs[i] = (o, new_parent)
        moved += 1
    added = patch.get("AddedObjects", [])
    if added:
        log.append("%d AddedObjects are NOT baked (unsupported here): add them to the prefab by hand" % len(added))
    return removed_objs, removed_comps, overrides, moved


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", default=os.path.join("Assets", "scenes", "boss_test.scene"))
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    scene_path = os.path.join(ROOT, args.scene)
    with open(scene_path, "r", encoding="utf-8") as f:
        scene = json.load(f)
    inst = None
    for g in scene["GameObjects"]:
        if "__Prefab" in g:
            inst = g
            break
    if inst is None:
        raise SystemExit("no prefab instance in " + scene_path)
    prefab_rel = inst["__Prefab"]
    prefab_path = os.path.join(ROOT, "Assets", prefab_rel.replace("/", os.sep))
    with open(prefab_path, "r", encoding="utf-8") as f:
        prefab = json.load(f)
    root = prefab["RootObject"]
    patch = inst.get("__PrefabInstancePatch") or {}
    before = len(index(root)[0])
    log = []
    ro, rc, ov, mv = apply_patch(root, patch, log)
    after = len(index(root)[0])
    for line in log:
        print("  !", line)
    print("%s: %d objects -> %d (removed %d objects / %d components, %d overrides, %d reordered)" % (prefab_rel, before, after, ro, rc, ov, mv))

    # the scene's instance: a clean patch on the baked prefab, the id map pruned to what is left
    root_overrides = {po["Property"]: po["Value"] for po in patch.get("PropertyOverrides", [])
                      if po["Target"]["IdValue"] == root["__guid"] and po["Target"]["Type"] == "GameObject"}
    clean = {
        "AddedObjects": [], "RemovedObjects": [],
        "PropertyOverrides": [
            {"Target": {"Type": "GameObject", "IdValue": root["__guid"]}, "Property": p, "Value": root_overrides.get(p, d)}
            for p, d in (("Position", "0,0,0"), ("Rotation", "0,0,0,1"), ("Name", "hell_arena"))
        ],
        "MovedObjects": [],
    }
    objs, comps = index(root)
    keep = set(objs) | set(comps)
    idmap = inst.get("__PrefabIdToInstanceId", {})
    pruned = {k: v for k, v in idmap.items() if k in keep}
    print("scene instance: patch cleaned, id map %d -> %d" % (len(idmap), len(pruned)))
    if args.dry_run:
        print("dry run: nothing written")
        return
    with open(prefab_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(prefab, f, indent=2, ensure_ascii=False)
    inst["__PrefabInstancePatch"] = clean
    inst["__PrefabIdToInstanceId"] = pruned
    with open(scene_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(scene, f, indent=2, ensure_ascii=False)
    print("wrote %s and %s" % (prefab_path, scene_path))


if __name__ == "__main__":
    main()
