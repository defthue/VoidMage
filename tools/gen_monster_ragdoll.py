"""Write each monster's ragdoll capsules (Assets/Characters/Monsters/<Title>/prefabs/<kind>_physicsshapelist.vmdl_prefab),
the same PhysicsShapeList format the skeletons use, which gen_monster_assets.py references from the vmdl.
The user's call: NO monster plays its death clip, every one of them ragdolls.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/gen_monster_ragdoll.py -- [--kind bat]

Unlike tools/gen_skeleton_ragdoll.py there is no hand-written bone table: eight rigs with nothing in common
(a snake is a chain, a spider is 24 leg bones, a bat has wings) would need eight of them, and the pack names
nothing consistently. The capsules are derived from the SKIN instead, which is the thing that actually says
how fat a bone is:

* a vertex belongs to the bone it is most weighted to; a bone with fewer than `MIN_VERTS` of them (a pole
  target) is never a candidate;
* a root-parented bone with no skinned descendants is an IK effector (the rat's, frog's and spider's feet):
  it gets NO body and a `weld` naming the nearest limb bone in the rest pose, which becomes
  `SkeletonBody.CorpseWelds` on the prefab. A body there would joint to the torso and swing from the belly;
* bodies go on SEGMENTS under a hard `ragdoll_max` (16, the skeleton's own number): each candidate is scored
  by the skin in its subtree that no kept descendant already moves, over the bodies its chain costs, and the
  chain closes over the FULL hierarchy so an unskinned control bone in the middle of a rig (`Body` on the
  spider) comes along as a connector rather than severing ten limbs. Fattest-bone-first spent the rat's
  budget on tail segments before its front legs; this marches down the spine and then out along every limb;
* every bone without a body is driven to ride its parent rigidly at runtime by Code/Npc/MonsterCorpse.cs,
  exactly as a skeleton's fingers ride its hand -- the engine does NOT do that by itself (ModelPhysics only
  overrides bones that own a body, and SkeletonCorpse.SeedPose freezes the rest to the corpse root);
* the capsule is ANCHORED AT THE BONE with gen_skeleton_ragdoll.py's formula, running from the head toward
  the first child, and the skin decides only its RADIUS (the MEDIAN perpendicular distance of its vertices
  from that axis), so a fat abdomen is fat and a wing bone is thin without a single authored number.
  SkeletonCorpse.BuildJoints anchors every joint at the bone origin, so a capsule that does not contain it
  is a lever arm the solver answers with torque;
* a connector, having no skin, is sized to the fattest body it links: at `min_radius` it was a
  near-weightless rod the whole creature hung off, and that is what exploded the snake and the spider;
* every bone name written for the engine goes through `bone_id`: s&box spells Blender's `FrontLeg.L` as
  `FrontLeg_L`, and a `parent_bone` that does not resolve is a body that is silently never created.

Units: capsule numbers are in the FBX's own bone space, which is the imported armature space times the
armature node's export scale (`arm.scale * 100`: Blender writes geometry in metres under a 100x node scale,
and the rat's armature carries an extra 0.395 of its own). The vmdl's scale modifier then applies to physics
as it does to the mesh.
"""
import argparse, json, os, statistics, sys

import bpy
from mathutils import Vector

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import monster_kinds  # noqa: E402

MIN_VERTS = 4
# 16, the number the hand-authored skeleton runs over its 46-bone rig, and it means the same thing here: one
# body per part you want to FLEX at. Every bone that does not get one is driven to ride its parent rigidly by
# Code/Npc/MonsterCorpse.cs, exactly as the skeleton's fingers ride its hand, so leaving a bone out costs the
# joint it would have bent at and nothing else.
DEFAULT_MAX_BODIES = 16
# a capsule thinner than this fraction of the model's longest axis reads as a stick and tunnels through floors
MIN_RADIUS_FRACTION = 0.012
# and one fatter than this fraction of its own BONE length is a beach ball: the bat's wing membrane and the
# snake's jaw own wide, flat vertex clouds whose spread has nothing to do with how thick the limb is
MAX_RADIUS_OF_LENGTH = 0.5
# no single body may be a big fraction of the whole creature. The skeleton's fattest capsule is 6.5 % of its
# height; the snake's mouth came out at 19 % of the snake and dominated the corpse's whole inertia.
MAX_RADIUS_OF_MODEL = 0.08


def log(*a):
    print("[ragdoll]", *a)


def bone_id(name):
    """Blender's name for a bone, spelled the way the ENGINE spells it.

    Blender names mirrored bones `FrontLeg.L`; s&box's compiled skeleton spells that `FrontLeg_L` (read
    straight out of spider.vmdl_c). `ModelPhysics.CreateParts` does

        var bone = bones.GetBone( part.BoneName );
        if ( bone == null || !dictionary.TryGetValue( bone, out var value ) ) continue;

    so a capsule whose `parent_bone` still has the dot resolves to nothing and its body is SILENTLY NEVER
    CREATED. That is not a small loss: the spider was running its whole corpse on the 4 capsules whose names
    happen to have no dot (Body, Abdomen, Thorax, Head) out of the 16 written here, with all eight legs
    missing, and the rat had 9 of 16 - which is why its legs looked dead. Every bone name that leaves this
    file for the engine goes through here."""
    return name.replace(".", "_")


def run(name, max_bodies):
    kind = monster_kinds.kind(name)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=kind["fbx"])
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH" and not o.name.endswith("_Outline"))
    bones = arm.data.bones
    unit = arm.scale.x * 100.0          # imported armature space -> the FBX's own bone units
    to_arm = arm.matrix_world.inverted() @ mesh.matrix_world

    # every vertex to the bone it is most weighted to
    group_bone = {g.index: g.name for g in mesh.vertex_groups}
    owned = {}
    for v in mesh.data.vertices:
        best, best_w = None, 0.0
        for g in v.groups:
            if g.weight > best_w and group_bone.get(g.group) in bones:
                best, best_w = group_bone[g.group], g.weight
        if best is not None:
            owned.setdefault(best, []).append(to_arm @ v.co)

    # the model's size in ARMATURE space, which is what every measurement below is in. mesh.dimensions is in
    # Blender's world units and is 100x smaller here, so using it made the min-radius floor dead code.
    all_points = [p for pts in owned.values() for p in pts]
    extent = max(max(p[i] for p in all_points) - min(p[i] for p in all_points) for i in range(3))
    min_radius = extent * MIN_RADIUS_FRACTION
    # armature units -> final game units, purely so the log reads in the same numbers as the skeleton's
    scale_to_units = unit * (json.load(open(kind["model_json"]))["scale"]
                             if os.path.exists(kind["model_json"]) else 1.0)
    skinned = {b for b in owned if len(owned[b]) >= MIN_VERTS}
    root_bone = next(b.name for b in bones if b.parent is None)

    def descendants(name):
        out, stack = [], [c.name for c in bones[name].children]
        while stack:
            n = stack.pop()
            out.append(n)
            stack.extend(c.name for c in bones[n].children)
        return out

    # These packs park their IK effectors -- the rat's and spider's feet, the snake's mouth -- directly on the
    # ARMATURE ROOT, hanging off nothing. A body there joints to the torso, so the foot swings from the belly;
    # what it wants is to ride the leg it visually belongs to. So it gets NO body and a follow override naming
    # the nearest real bone in the rest pose, and Code/Npc/MonsterCorpse.cs drives it there every frame.
    effectors = {b for b in skinned
                 if bones[b].parent is not None and bones[b].parent.name == root_bone
                 and not any(d in skinned for d in descendants(b))}
    skinned -= effectors

    # Bodies go on SEGMENTS, the way the skeleton's 16 were chosen (pelvis, 2 spine, head, 4 limbs x 3) -- NOT
    # on the fattest bones, which is what spent the rat's budget on tail segments before its front legs ever
    # got one. Score each candidate by the skin in its subtree that no kept descendant already moves, over the
    # number of bodies it costs: its whole chain up to something already kept, closed over the FULL hierarchy
    # (one unskinned control bone in the middle of a rig -- `Body` on the spider -- severed ten limbs into
    # free-floating debris). That marches down the spine and then out along every limb, instead of piling the
    # whole budget into one fat region.
    subtree = {b.name: len(owned.get(b.name, ())) + sum(len(owned.get(d, ())) for d in descendants(b.name))
               for b in bones}
    keep, connectors = [], set()

    def chain_to_kept(bone_name):
        chain, b = [], bones[bone_name].parent
        while b is not None and b.name not in keep:
            if b.name != root_bone:
                chain.append(b.name)
            b = b.parent
        return list(reversed(chain)) + [bone_name]

    while len(keep) < max_bodies:
        best, best_score, best_chain = None, 0.0, None
        for bone_name in sorted(skinned):
            if bone_name in keep:
                continue
            chain = chain_to_kept(bone_name)
            if len(keep) + len(chain) > max_bodies:
                continue
            below = set(descendants(bone_name))
            gain = subtree[bone_name] - sum(subtree[k] for k in keep if k in below)
            score = gain / float(len(chain))
            if score > best_score:
                best, best_score, best_chain = bone_name, score, chain
        if best is None:
            break
        connectors.update(b for b in best_chain if b not in skinned)
        keep.extend(best_chain)

    # If more than one chain hangs off the armature root the ragdoll has several free roots, which
    # SkeletonCorpse cannot joint together (it leaves a body with no bodied ancestor loose). Give the root
    # itself a body so everything descends from one bone; if that breaks the budget, hand back the kept leaf
    # that moves the least skin.
    if len([b for b in keep if bones[b].parent is not None and bones[b].parent.name == root_bone]) > 1:
        keep.insert(0, root_bone)
        connectors.add(root_bone)
        while len(keep) > max_bodies:
            leaves = [b for b in keep if b != root_bone and not any(d in keep for d in descendants(b))]
            drop = min(leaves, key=lambda b: subtree[b])
            keep.remove(drop)
            connectors.discard(drop)
    keep = set(keep)

    # Everything without a body rides its parent rigidly at runtime, which is free and needs no data. The
    # effectors are the exception: their parent IS the armature root, so they would ride the torso.
    def nearest_bone(bone_name):
        h = bones[bone_name].head_local
        options = [b for b in bones if b.name not in effectors and b.name != root_bone and b.name != bone_name]
        return min(options, key=lambda b: min((h - b.head_local).length, (h - b.tail_local).length)).name

    welds = {b: nearest_bone(b) for b in sorted(effectors)}
    unowned = sum(len(v) for b, v in owned.items() if b not in keep)
    log("%s: %d bones, %d with skin, %d bodies (%d connectors: %s)" % (
        name, len(bones), len(skinned), len(keep), len(connectors), ", ".join(sorted(connectors)) or "none"))
    log("  %d of %d verts (%.0f%%) ride a parent rigidly -- the skeleton's fingers do the same" % (
        unowned, len(mesh.data.vertices), 100.0 * unowned / max(len(mesh.data.vertices), 1)))
    for b, target in welds.items():
        log("  weld %-16s -> %s (root-parented in the rig, so it cannot just follow its parent)" % (b, target))

    order = sorted(keep, key=lambda b: bones.find(b))
    geom = {}
    for bone_name in order:
        b = bones[bone_name]
        frame = b.matrix_local.to_3x3()
        child = next((c for c in b.children), None)
        direction = (child.head_local - b.head_local) if child is not None else (b.tail_local - b.head_local)
        bone_length = direction.length
        if bone_length < 1e-5:
            direction, bone_length = (b.tail_local - b.head_local), max(b.length, 1e-5)
        axis_arm = direction.normalized()
        head = b.head_local
        verts = owned.get(bone_name, ())
        # The skin decides the RADIUS and nothing else; see the capsule geometry below. A connector owns no
        # skin, so it is left None here and sized in the second pass.
        radius = None
        if verts:
            offs = [((v - head) - axis_arm * (v - head).dot(axis_arm)).length for v in verts]
            radius = statistics.median(offs)
        geom[bone_name] = dict(axis=(frame.inverted() @ axis_arm).normalized(), length=bone_length,
                               verts=len(verts), radius=radius)

    def clamp(radius, bone_length):
        # never a beach ball, and never a big fraction of the creature (the snake's mouth owned 617 of its 815
        # vertices and came out 19 % of the whole snake, dominating the corpse's entire inertia)
        return max(min(radius, bone_length * MAX_RADIUS_OF_LENGTH, extent * MAX_RADIUS_OF_MODEL), min_radius)

    # A CONNECTOR MUST BE AS SUBSTANTIAL AS WHAT IT CARRIES. Mass comes from capsule volume, so giving a
    # skinless bone the minimum radius made it near-weightless -- and a connector is precisely the bone the
    # whole creature hangs off. The spider's `Body` was a 9.5-long rod of mass 17 with TEN bodies and 1098 of
    # mass on it (65x its own); the snake's `Body` carried its chest at 34x and its `Root` the tail at 25x.
    # Torque at a joint scales with the lever arm, so load ratio x connector length is the number that matters,
    # and it separated the two exploding creatures from the six working ones by more than 4x. Sizing a
    # connector to the fattest body it links costs nothing visually - it is physics-only geometry, and
    # ragdoll-vs-ragdoll collision is Ignore.
    def bodied_parent(bone_name):
        p = bones[bone_name].parent
        while p is not None and p.name not in keep:
            p = p.parent
        return p.name if p is not None else None

    linked = {}
    for bone_name in order:
        p = bodied_parent(bone_name)
        if p is not None:
            linked.setdefault(p, set()).add(bone_name)
            linked.setdefault(bone_name, set()).add(p)
    for _ in range(len(order)):          # a connector may link only to other connectors (the snake's Root)
        if all(geom[b]["radius"] is not None for b in order):
            break
        for bone_name in order:
            if geom[bone_name]["radius"] is not None:
                continue
            known = [geom[n]["radius"] for n in linked.get(bone_name, ()) if geom[n]["radius"] is not None]
            if known:
                geom[bone_name]["radius"] = max(known)
    for bone_name in order:
        if geom[bone_name]["radius"] is None:
            geom[bone_name]["radius"] = min_radius

    shapes, count = "", 0
    for bone_name in order:
        g = geom[bone_name]
        axis, bone_length = g["axis"], g["length"]
        radius = clamp(g["radius"], bone_length)
        # ANCHOR THE CAPSULE AT THE BONE, exactly as gen_skeleton_ragdoll.py does.
        # SkeletonCorpse.BuildJoints anchors every ball joint at Vector3.Zero in the child bone's frame -- the
        # bone ORIGIN -- so a capsule fitted to wherever the skin happens to sit (the snake's head starts 47
        # units down its own bone) leaves the joint pulling on a point OUTSIDE the body it drives. The solver
        # answers a lever arm with torque, and that is what made these corpses flail while the skeleton, whose
        # 16 capsules every one contain their own bone origin, never did.
        inset = min(radius * 0.7, bone_length * 0.2)
        a = inset
        z = max(bone_length - radius * 0.7, bone_length * 0.6)
        p0, p1 = axis * a, axis * z
        seg = z - a
        # inset <= 0.2L and z >= 0.6L, so the segment can never collapse to a sphere and the bone origin is
        # always inside the capsule volume (inset <= 0.7 * radius). Both are load-bearing; assert them.
        if seg < 1e-4 or inset > radius:
            raise SystemExit("%s: %s capsule is wrong (segment %.4f, radius %.4f, inset %.4f) - it must be an "
                             "elongated capsule containing its own bone origin" % (name, bone_name, seg, radius, inset))
        log("  %-14s verts %4d  radius %6.2f  segment %6.2f  r/L %5.2f  %2.0f%% of the model%s" % (
            bone_name, g["verts"], radius * scale_to_units, seg * scale_to_units, radius / seg,
            100.0 * radius / extent, "  (connector, sized to what it carries)" if bone_name in connectors else ""))
        shapes += f'''							{{
								_class = "PhysicsShapeCapsule"
								parent_bone = "{bone_id(bone_name)}"
								surface_prop = "flesh"
								collision_tags = "ragdoll"
								radius = {radius * unit:.6f}
								point0 = [ {p0.x * unit:.6f}, {p0.y * unit:.6f}, {p0.z * unit:.6f} ]
								point1 = [ {p1.x * unit:.6f}, {p1.y * unit:.6f}, {p1.z * unit:.6f} ]
							}},
'''
        count += 1

    out = f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc29:version{{3cec427c-1b0e-4d48-a90a-0436f33a6041}} -->
{{
	rootNode =
	{{
		_class = "RootNode"
		children =
		[
			{{
				_class = "PhysicsShapeList"
				children =
				[
					{{
						_class = "Folder"
						name = "PhysicsBody"
						children =
						[
{shapes}						]
					}},
				]
			}},
		]
	}}
}}
'''
    os.makedirs(os.path.dirname(kind["physics_prefab"]), exist_ok=True)
    with open(kind["physics_prefab"], "w", newline="\n") as f:
        f.write(out)
    # what each bone carries and what got a body, so verify_monsters.py can catch "this limb will freeze"
    # without needing Blender
    with open(kind["ragdoll_json"], "w", newline="\n") as f:
        # every name here is in ENGINE spelling (see bone_id): the welds become CorpseWelds on the prefab and
        # are looked up against Model.Bones at runtime, so a dot in one is a weld that silently does nothing
        json.dump(dict(kind=name, bodies=sorted(bone_id(b) for b in keep),
                       connectors=sorted(bone_id(b) for b in connectors),
                       welds={bone_id(b): bone_id(t) for b, t in welds.items()},
                       verts={bone_id(b): len(v) for b, v in owned.items()},
                       total_verts=len(mesh.data.vertices)),
                  f, indent=2)
    log("  wrote %s (%d capsules, bone unit %.2f)" % (
        os.path.relpath(kind["physics_prefab"], monster_kinds.ROOT), count, unit))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--max-bodies", type=int, default=DEFAULT_MAX_BODIES)
    monster_kinds.add_kind_arg(ap)
    args = ap.parse_args(argv)
    for name in monster_kinds.selected(args.kind):
        run(name, monster_kinds.KINDS[name].get("ragdoll_max", args.max_bodies))


if __name__ == "__main__":
    main()
