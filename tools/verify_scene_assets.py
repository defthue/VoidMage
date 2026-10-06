"""Check that every asset a scene references actually exists on disk.

  python tools/verify_scene_assets.py Assets/scenes/foo.scene [more.scene ...]

Walks scene -> vmdl -> vmat -> texture and reports anything dangling. This is what catches a
pack copied across without its atlases: the scene loads, the models load, and every surface
renders as the missing-material checkerboard.

Also reports the scene's world extents in inches/metres so a scale slip is obvious before the
editor is ever opened.
"""
import json, os, re, sys, collections

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
ASSETS = os.path.join(PROJ, "Assets")


def asset_path(rel):
    return os.path.join(ASSETS, rel.replace("/", os.sep))


def walk_objects(gos, depth=0, parent=(0.0, 0.0, 0.0)):
    for g in gos:
        p = [float(x) for x in g.get("Position", "0,0,0").split(",")]
        world = tuple(parent[i] + p[i] for i in range(3))
        yield g, world
        yield from walk_objects(g.get("Children", []), depth + 1, world)


def main():
    total_problems = 0
    for scene_rel in sys.argv[1:]:
        path = scene_rel if os.path.isabs(scene_rel) else os.path.join(PROJ, scene_rel)
        doc = json.load(open(path, encoding="utf-8"))
        print("=" * 70)
        print(os.path.relpath(path, PROJ))

        models = collections.Counter()
        comps = collections.Counter()
        positions = []
        for g, world in walk_objects(doc["GameObjects"]):
            for c in g.get("Components", []):
                comps[c["__type"]] += 1
                m = c.get("Model")
                if m:
                    models[m] += 1
                    positions.append(world)

        print("  components:", ", ".join("%s x%d" % (k.split(".")[-1], v) for k, v in comps.most_common()))
        print("  model refs: %d across %d distinct vmdls" % (sum(models.values()), len(models)))

        problems = []
        vmats = collections.Counter()
        for m in models:
            p = asset_path(m)
            if not os.path.exists(p):
                problems.append("missing vmdl: %s" % m)
                continue
            body = open(p, encoding="utf-8", errors="replace").read()
            for t in re.findall(r'to\s*=\s*"([^"]+)"', body):
                vmats[t] += 1
            for f in re.findall(r'filename\s*=\s*"([^"]+)"', body):
                if not os.path.exists(asset_path(f)):
                    problems.append("%s -> missing mesh file %s" % (m, f))

        print("  vmats     : %d distinct" % len(vmats))
        textures = collections.Counter()
        for v in vmats:
            p = asset_path(v)
            if not os.path.exists(p):
                problems.append("missing vmat: %s (used by %d models)" % (v, vmats[v]))
                continue
            body = open(p, encoding="utf-8", errors="replace").read()
            sh = re.search(r'shader\s+"([^"]+)"', body)
            if sh and not os.path.exists(asset_path(sh.group(1))):
                problems.append("%s -> missing shader %s" % (v, sh.group(1)))
            for t in re.findall(r'"([^"]+\.(?:png|tga|jpg))"', body):
                textures[t] += 1
        for t in textures:
            if not os.path.exists(asset_path(t)):
                problems.append("missing texture: %s" % t)
        print("  textures  : %d distinct" % len(textures))

        if positions:
            for i, ax in enumerate("xyz"):
                lo = min(p[i] for p in positions)
                hi = max(p[i] for p in positions)
                print("  extent %s  : %10.1f .. %10.1f in   (%7.1f .. %7.1f m)"
                      % (ax, lo, hi, lo / 39.3701, hi / 39.3701))

        if problems:
            print("  PROBLEMS (%d):" % len(problems))
            for p in problems[:40]:
                print("    -", p)
            if len(problems) > 40:
                print("    ... and %d more" % (len(problems) - 40))
        else:
            print("  OK - every vmdl, mesh file, vmat, shader and texture resolves")
        total_problems += len(problems)

    print("=" * 70)
    print("total problems:", total_problems)
    return 1 if total_problems else 0


if __name__ == "__main__":
    sys.exit(main())
