"""Walk every monster's chain and report anything dangling, without opening the editor:

    prefab -> vmdl + animgraph + brain
    vmdl   -> mesh FBX (and the mesh objects it names) -> vmats -> shaders -> textures, physics shape list,
              the clip FBX behind every AnimFile, and the animgraph it points at
    graph  -> every clip name it plays must be an AnimFile in that vmdl, and every parameter SkeletonBody
              writes must exist (a missing one is silent at runtime and the monster simply never animates)

    python tools/verify_monsters.py
"""
import json, math, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from fbx_bounds import parse, find  # noqa: E402
import monster_kinds  # noqa: E402

# A ragdoll should be ONE tree: exactly one body with no bodied ancestor, its root. SkeletonCorpse.BuildJoints
# joints each body to its nearest ancestor that owns one and leaves it UNCONSTRAINED when there is none, so a
# second free root is a rigidbody that simply rolls away from the corpse. The generator gives the armature
# root a body whenever more than one chain hangs off it, precisely so this never happens.
MAX_UNPARENTED = 1

ASSETS = os.path.join(ROOT, "Assets")
# what Code/Npc/SkeletonBody.cs sets on the renderer every frame
BODY_PARAMS = ("move_x", "move_y", "guard", "busy", "attack_kind", "attack_trigger", "hurt_kind", "hurt_trigger", "dead")

problems = []


def check(condition, message):
    if not condition:
        problems.append(message)
    return condition


def asset(rel):
    return os.path.join(ASSETS, rel.replace("/", os.sep))


def bone_parents(fbx):
    """{bone: parent bone or None} out of the FBX's Model graph.

    THE TRAP: a bone Model is ALSO an `OO` child of the skin cluster that deforms with it, so taking the last
    `OO` link for each bone reports every bone as a root and hides a broken ragdoll completely. Only a
    connection whose PARENT is itself a Model gives the real bone parent."""
    _, nodes = parse(fbx)
    names, kinds = {}, {}
    for objs in find(nodes, "Objects"):
        for o in objs[2]:
            if o[0] == "Model":
                names[o[1][0]] = o[1][1].decode("latin1").split("\x00")[0]
                kinds[o[1][0]] = o[1][2].decode("latin1")
    parent = {}
    for conns in find(nodes, "Connections"):
        for c in conns[2]:
            if c[0] == "C" and c[1][0] == b"OO" and c[1][1] in names and c[1][2] in names:
                parent[c[1][1]] = c[1][2]
    # ENGINE SPELLING, not Blender's: s&box's compiled skeleton writes `FrontLeg.L` as `FrontLeg_L`, and
    # ModelPhysics.CreateParts silently skips any physics part whose bone name does not resolve. Everything in
    # this file compares against the generated data, which is already in engine spelling.
    def spell(n):
        return n.replace(".", "_") if n else n

    return {spell(n): (spell(names.get(parent.get(i))) if kinds.get(parent.get(i)) == "LimbNode" else None)
            for i, n in names.items() if kinds[i] == "LimbNode"}


CAPSULE = re.compile(r'parent_bone = "([^"]+)"\s*\n\s*surface_prop[^\n]*\n\s*collision_tags[^\n]*\n\s*'
                     r'radius = ([-\d.eE+]+)\s*\n\s*point0 = \[ ([^\]]+) \]\s*\n\s*point1 = \[ ([^\]]+) \]')


def bodied_parent(bone, parents, owned):
    """The body this one's joint hangs from: the nearest ancestor that owns a body, as SkeletonCorpse does."""
    a = parents.get(bone)
    while a is not None and a not in owned:
        a = parents.get(a)
    return a


def check_ragdoll(kind, name):
    """Every ragdoll body must reach the root through bones that also own a body, or the corpse tears apart."""
    if not os.path.exists(kind["physics_prefab"]):
        return
    text = open(kind["physics_prefab"]).read()
    bodies = re.findall(r'parent_bone = "([^"]+)"', text)
    parents = bone_parents(kind["fbx"])

    # THE CAPSULE MUST CONTAIN ITS OWN BONE ORIGIN. SkeletonCorpse.BuildJoints anchors every ball joint at
    # Vector3.Zero in the child bone's frame, so a capsule fitted to where the skin happens to sit leaves the
    # joint pulling on a point OUTSIDE the body it drives; the solver answers that lever arm with torque, and
    # it is what made these corpses flail while the skeleton's, which all contain their bone origin, never did.
    caps = {}
    for bone, radius, p0, p1 in CAPSULE.findall(text):
        r = float(radius)
        a = [float(v) for v in p0.split(",")]
        b = [float(v) for v in p1.split(",")]
        offset = sum(v * v for v in a) ** 0.5
        segment = sum((b[i] - a[i]) ** 2 for i in range(3)) ** 0.5
        caps[bone] = (r, segment)
        # A DOT IN A BONE NAME IS A BODY THAT NEVER EXISTS. ModelPhysics.CreateParts does
        # `bones.GetBone(part.BoneName)` and `continue`s on null, with no warning; s&box's compiled skeleton
        # spells Blender's `FrontLeg.L` as `FrontLeg_L`. The spider was silently ragdolling on the 4 of its 16
        # capsules whose names happened to have no dot, and the rat on 9 of 16 - which is why its legs looked
        # dead through three rounds of fixing everything except this.
        check(re.fullmatch(r"[A-Za-z0-9_]+", bone) is not None,
              "%s: ragdoll capsule on %r - the engine spells bone names [A-Za-z0-9_] only, so this body would "
              "never be created and the limb would be dead" % (name, bone))
        check(offset <= r * 1.001,
              "%s: the %s capsule starts %.2f from its bone origin but is only %.2f thick - the joint would "
              "pull on a point outside the body and the corpse will flail" % (name, bone, offset, r))
        check(segment > r * 0.05,
              "%s: the %s capsule is a SPHERE (segment %.3f, radius %.3f) - it resists no rotation at all"
              % (name, bone, segment, r))
    owned = set(bodies)
    for b in bodies:
        check(b in parents, "%s: ragdoll capsule on %r, which is not a bone of this rig" % (name, b))
    unparented = []
    for b in bodies:
        a = parents.get(b)
        while a is not None and a not in owned:
            a = parents.get(a)
        if a is None:
            unparented.append(b)
    check(len(unparented) <= MAX_UNPARENTED,
          "%s: %d ragdoll bodies have no bodied ancestor (%s) - the corpse would come apart; the generator "
          "should be adding connector bones" % (name, len(unparented), ", ".join(unparented)))
    # A bone with no body does NOT follow its parent by itself: ModelPhysics only ever overrides bones that own
    # a body, and SkeletonCorpse.SeedPose stamps the rest once, so they freeze relative to the corpse root.
    # Code/Npc/MonsterCorpse.cs drives them from their parent instead - but only if that parent chain actually
    # reaches something driven. Anything that does not is genuinely frozen, and it is the defect that had half
    # the rat's mesh nailed to the death spot while the rest tumbled away.
    welded, frozen = 0, []
    if os.path.exists(kind["ragdoll_json"]):
        rag = json.load(open(kind["ragdoll_json"]))
        welds = rag.get("welds", {})
        # A CONNECTOR MUST BE AS SUBSTANTIAL AS WHAT IT CARRIES. Mass is capsule volume, and a skinless bone
        # given the minimum radius becomes a near-weightless rod that the whole creature hangs off. Torque at a
        # joint scales with the lever arm, so load-ratio x connector length is the number that matters: it was
        # 615 on the spider and 407 on the snake, the two that exploded, against <= 72 on the six that did not.
        scale = json.load(open(kind["model_json"]))["scale"] if os.path.exists(kind["model_json"]) else 1.0
        mass = {b: math.pi * (r * scale) ** 2 * (L * scale) + 4.0 / 3.0 * math.pi * (r * scale) ** 3
                for b, (r, L) in caps.items()}
        for conn in rag.get("connectors", ()):
            if conn not in mass:
                continue
            load = sum(m for b, m in mass.items() if b != conn and bodied_parent(b, parents, owned) == conn)
            metric = (load / max(mass[conn], 1e-9)) * caps[conn][1] * scale
            check(metric <= 100.0,
                  "%s: the %s connector carries %.0fx its own mass over a %.1f-unit lever (metric %.0f, the "
                  "exploding snake and spider were 407 and 615) - size it to the fattest body it links"
                  % (name, conn, load / max(mass[conn], 1e-9), caps[conn][1] * scale, metric))
        for bone, verts in rag["verts"].items():
            if bone in owned:
                continue
            welded += 1
            seen, at = set(), welds.get(bone, parents.get(bone))
            while at is not None and at not in owned and at not in seen:
                seen.add(at)
                at = welds.get(at, parents.get(at))
            if at is None:
                frozen.append((verts, bone))
        frozen.sort(reverse=True)
        loose = sum(v for v, _ in frozen)
        check(loose <= rag["total_verts"] * 0.03,
              "%s: %d of %d verts (%.0f%%) are on bones nothing drives - they freeze at the death spot while "
              "the body tumbles away: %s" % (name, loose, rag["total_verts"], 100.0 * loose / rag["total_verts"],
                                             ", ".join("%s (%d)" % (b, v) for v, b in frozen[:6])))
    return len(bodies), len(unparented), welded


def main():
    for name in sorted(monster_kinds.KINDS):
        k = monster_kinds.kind(name)
        print("==", name)

        for label, path in (("fbx", k["fbx"]), ("vmdl", k["vmdl"]), ("animgraph", k["animgraph"]),
                            ("prefab", k["prefab"]), ("brain", k["brain"]), ("ragdoll", k["physics_prefab"])):
            check(os.path.exists(path), "%s: no %s at %s" % (name, label, os.path.relpath(path, ROOT)))

        vmdl = open(k["vmdl"]).read()
        clips = re.findall(r'_class = "AnimFile"\s*\n\s*name = "([^"]+)"', vmdl)
        sources = re.findall(r'source_filename = "([^"]+)"', vmdl)
        for src in sources:
            check(os.path.exists(asset(src)), "%s: vmdl clip file missing: %s" % (name, src))
        for to in re.findall(r'to = "([^"]+)"', vmdl):
            check(os.path.exists(asset(to)), "%s: vmdl remaps to a missing vmat: %s" % (name, to))
        for target in re.findall(r'target_file = "([^"]+)"', vmdl):
            check(os.path.exists(asset(target)), "%s: vmdl references a missing file: %s" % (name, target))
        graph_in_vmdl = re.search(r'anim_graph_name = "([^"]*)"', vmdl).group(1)
        check(graph_in_vmdl == k["animgraph_rel"], "%s: vmdl animgraph is %r, expected %r" % (name, graph_in_vmdl, k["animgraph_rel"]))

        # the vmats the vmdl remaps to must reference textures and shaders that exist
        for to in set(re.findall(r'to = "([^"]+\.vmat)"', vmdl)):
            if not os.path.exists(asset(to)):
                continue
            body = open(asset(to)).read()
            for ref in re.findall(r'"([\w/]+\.(?:png|shader))"', body):
                check(os.path.exists(asset(ref)), "%s: %s references a missing %s" % (name, to, ref))

        graph = open(k["animgraph"]).read()
        played = set(re.findall(r'm_sequenceName = "([^"]+)"', graph))
        missing = sorted(played - set(clips))
        check(not missing, "%s: the animgraph plays clips the vmdl does not carry: %s" % (name, missing))
        params = set(re.findall(r'm_name = "([^"]+)"\s*\n\s*m_id', graph)) | set(re.findall(r'm_name = "([^"]+)"', graph))
        for p in BODY_PARAMS:
            check(p in params, "%s: animgraph has no `%s` parameter (SkeletonBody writes it every frame)" % (name, p))
        # EVERY attack kind SkeletonNpc can emit must have a state. Damage is dealt on a wall clock that never
        # consults the graph, so a kind with no state behind it is a hit with no animation - which is exactly
        # what the one-attack monsters were doing half the time, kind 2 having been left out.
        if k["attacks"]:
            states = set(re.findall(r'm_name = "(attack_\d+)"', graph))
            missing = [kind_id for kind_id in (1, 2, 3, 4) if "attack_%d" % kind_id not in states]
            check(not missing, "%s: animgraph has no state for attack_kind %s - SkeletonNpc can emit those and "
                               "would damage the player with nothing playing" % (name, missing))

        prefab = json.load(open(k["prefab"]))
        root = prefab["RootObject"]
        check("npc" in root["Tags"] and "enemy" in root["Tags"],
              "%s: prefab tags are %r; SkeletonSpawner requires enemy and npc" % (name, root["Tags"]))
        renderer = root["Children"][0]["Components"][0]
        check(renderer["Model"] == k["vmdl_rel"], "%s: prefab model is %r" % (name, renderer["Model"]))
        check(renderer["AnimationGraph"] == k["animgraph_rel"], "%s: prefab animgraph is %r" % (name, renderer["AnimationGraph"]))
        npc = next(c for c in root["Components"] if c["__type"].endswith("SkeletonNpc"))
        for slot in ("SwordA", "SwordB"):
            if slot in npc:
                check(npc[slot]["Name"] in clips, "%s: %s plays %r, which is not a clip in the vmdl" % (name, slot, npc[slot]["Name"]))
        # the brain the spawner will look up is the resource's own file name
        check(os.path.splitext(os.path.basename(k["brain"]))[0] == "monster_" + name,
              "%s: brain file name does not match the kind" % name)

        ragdoll = check_ragdoll(k, name)
        print("   %2d clips, %2d played by the graph, ragdoll %s, model %s, brain %s"
              % (len(clips), len(played),
                 ("%d bodies (%d free root%s), %d bones ride a parent" % (ragdoll[0], ragdoll[1],
                  "" if ragdoll[1] == 1 else "s - THE CORPSE WILL COME APART", ragdoll[2])) if ragdoll else "NONE",
                 renderer["Model"], os.path.basename(k["brain"])))

    generated = os.path.join(ROOT, "Code", "Npc", "MonsterKinds.Generated.cs")
    if check(os.path.exists(generated), "MonsterKinds.Generated.cs is missing"):
        cs = open(generated).read()
        for name in monster_kinds.KINDS:
            check('"%s"' % name in cs, "%s is not in MonsterKinds.Generated.cs" % name)
        for prefab_rel in re.findall(r'"(prefabs/[^"]+)"', cs):
            check(os.path.exists(asset(prefab_rel)), "MonsterKinds.Generated.cs points at a missing prefab: %s" % prefab_rel)

    print()
    if problems:
        print("%d PROBLEM(S):" % len(problems))
        for p in problems:
            print("  -", p)
        raise SystemExit(1)
    print("all monsters check out")


if __name__ == "__main__":
    main()
