"""Clone a skeleton prefab for a new kind (tools/skeleton_kinds.py), the way skeleton_soldier_02.prefab was made from
skeleton_soldier_01.prefab by hand: same components and values, fresh GUIDs everywhere, the kind's name and model.

    python tools/gen_skeleton_prefab.py --kind realms_01 [--from skeleton_soldier_01] [--force]

Writes Assets/Prefabs/skeleton_<kind>.prefab. Refuses to overwrite an existing prefab unless --force, because the
editor may have tuned it since (DamageScale, GuardPoints, capsule...). What changes: every `__guid` (root, each
component, each child), the root `Name`, SkeletonNpc.KindName, and the SkinnedModelRenderer's `Model`
(the kind's vmdl path relative to Assets, lowercase, the way the editor writes it). A kind may name its source
prefab itself (`prefab_from`, used when --from is not given) and override component values on top of it:
`prefab_npc` onto SkeletonNpc (the corpse's GuardPoints 0), `prefab_body` onto SkeletonBody (its Bleeds). Register
the kind in SkeletonSpawner.Kinds by hand; that table is not generated.
"""
import argparse, json, os, sys, uuid

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import skeleton_kinds  # noqa: E402

PREFABS = os.path.join(ROOT, "Assets", "Prefabs")


def fresh_guids(node):
    if isinstance(node, dict):
        for k, v in node.items():
            if k == "__guid" and isinstance(v, str):
                node[k] = str(uuid.uuid4())
            else:
                fresh_guids(v)
    elif isinstance(node, list):
        for v in node:
            fresh_guids(v)


def components(node, type_name):
    """Every component dict of that __type anywhere in the object tree."""
    found = []
    if isinstance(node, dict):
        if node.get("__type") == type_name:
            found.append(node)
        for v in node.values():
            found.extend(components(v, type_name))
    elif isinstance(node, list):
        for v in node:
            found.extend(components(v, type_name))
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--from", dest="source", default="", help="prefab name to clone (Assets/Prefabs); default the kind's prefab_from, else skeleton_soldier_01")
    ap.add_argument("--force", action="store_true", help="overwrite an existing prefab")
    skeleton_kinds.add_kind_arg(ap)
    args = ap.parse_args()
    kind = skeleton_kinds.kind(args.kind)
    source = args.source or kind.get("prefab_from") or "skeleton_soldier_01"
    src = os.path.join(PREFABS, source + ".prefab")
    name = "skeleton_" + kind["name"]
    dst = os.path.join(PREFABS, name + ".prefab")
    if os.path.exists(dst) and not args.force:
        raise SystemExit("%s exists; --force to overwrite (the editor may have tuned it)" % dst)
    with open(src, encoding="utf-8") as f:
        data = json.load(f)
    root = data["RootObject"]
    fresh_guids(data)
    root["Name"] = name
    npcs = components(root, "VoidMage.Npc.SkeletonNpc")
    renderers = components(root, "Sandbox.SkinnedModelRenderer")
    if len(npcs) != 1 or len(renderers) != 1:
        raise SystemExit("expected one SkeletonNpc and one SkinnedModelRenderer in %s, got %d / %d" % (src, len(npcs), len(renderers)))
    npcs[0]["KindName"] = kind["name"]
    model = os.path.relpath(kind["vmdl"], os.path.join(ROOT, "Assets")).replace("\\", "/").lower()
    renderers[0]["Model"] = model
    overrides = []
    for key, type_name in (("prefab_npc", "VoidMage.Npc.SkeletonNpc"), ("prefab_body", "VoidMage.Npc.SkeletonBody")):
        values = kind.get(key) or {}
        if not values:
            continue
        comps = components(root, type_name)
        if len(comps) != 1:
            raise SystemExit("expected one %s in %s for %s, got %d" % (type_name, src, key, len(comps)))
        for k, v in values.items():
            comps[0][k] = v
            overrides.append("%s.%s=%r" % (type_name.split(".")[-1], k, v))
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print("wrote", dst, "from", source, "model", model, "kind", kind["name"],
          "DamageScale", npcs[0].get("DamageScale", 1), "GuardPoints", npcs[0].get("GuardPoints", 2),
          "overrides", overrides or "none")


if __name__ == "__main__":
    main()
