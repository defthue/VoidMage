"""Write one skeleton kind's ragdoll shapes (tools/skeleton_kinds.py, e.g.
Assets/Characters/SkeletonSlave/prefabs/skeleton_slave_01_physicsshapelist.vmdl_prefab), heden's goblin format (a PhysicsShapeList of capsules parented to bones, surface flesh, collision tag `ragdoll`),
which gen_skeleton_npc_vmdl.py references from the vmdl. Joints are NOT authored: SkeletonCorpse builds ball joints
at runtime from the bone hierarchy (heden's proven fallback), so only the capsules live in the model.

Runs headless in Blender against the prepared skeleton FBX (bones are in cm, the vmdl's 0.3937 scale modifier
applies to physics too, so the numbers here are cm). Each capsule runs along the bone's own axis, read off the
rest pose (the direction to the first child, in the bone's frame; Blender-exported bones point along +Y and the
script prints the axis it found so a wrong convention is visible, not silent).

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup --python tools/gen_skeleton_ragdoll.py -- --kind soldier_01

Any other rig with the same Epic bone names (the player's CharacterToon human, same cm units and the same 0.3937
vmdl scale) takes the FBX and the output path directly instead of a kind:

    ... --python tools/gen_skeleton_ragdoll.py -- --fbx Assets/CharacterToon/human_torso.fbx --out Assets/CharacterToon/prefabs/human_base_physicsshapelist.vmdl_prefab
"""
import argparse, os, sys

import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import skeleton_kinds  # noqa: E402

# folder -> [(bone, radius cm, child bone the capsule runs toward or None = the bone's own tail, extra length cm)]
BODIES = {
    "PhysicsBody": [("pelvis", 11.0, "spine_01", 0.0), ("spine_02", 10.0, "spine_03", 0.0), ("spine_03", 12.0, "neck_01", 8.0), ("head", 10.0, None, 0.0)],
    "PhysicsArms": [("upperarm_l", 4.5, "lowerarm_l", 0.0), ("upperarm_r", 4.5, "lowerarm_r", 0.0),
                    ("lowerarm_l", 3.5, "hand_l", 0.0), ("lowerarm_r", 3.5, "hand_r", 0.0),
                    ("hand_l", 4.0, None, 0.0), ("hand_r", 4.0, None, 0.0)],
    "PhysicsLegs": [("thigh_l", 6.0, "calf_l", 0.0), ("thigh_r", 6.0, "calf_r", 0.0),
                    ("calf_l", 4.5, "foot_l", 0.0), ("calf_r", 4.5, "foot_r", 0.0),
                    ("foot_l", 4.5, None, 0.0), ("foot_r", 4.5, None, 0.0)],
}


def log(*a):
    print("[ragdoll]", *a)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    skeleton_kinds.add_kind_arg(ap)
    ap.add_argument("--fbx", help="any FBX with the Epic bone names, bypasses --kind (pair with --out)")
    ap.add_argument("--out", help="the .vmdl_prefab to write when --fbx is given")
    args = ap.parse_args(argv)
    if args.fbx:
        if not args.out:
            raise SystemExit("--fbx needs --out")
        kind = {}
        FBX, OUT = os.path.join(ROOT, args.fbx), os.path.join(ROOT, args.out)
    else:
        kind = skeleton_kinds.kind(args.kind)
        FBX, OUT = kind["fbx"], kind["physics_prefab"]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
    # bone rest data is already in cm (the scene keeps cm data under a 0.01 OBJECT scale, which is not applied here)
    unit = 1.0
    bones = arm.data.bones

    folders = ""
    count = 0
    for folder, entries in BODIES.items():
        if folder in kind.get("ragdoll_skip", ()):
            log(folder, "skipped for this kind")
            continue
        shapes = ""
        for name, radius, child, extra in entries:
            if name not in bones:
                log("%-12s missing on this rig, skipped" % name)   # the legless Tormented Soul has no feet
                continue
            if child is not None and child not in bones:
                child = None                                       # a calf with no foot: terminal, continues its direction
            b = bones[name]
            frame = b.matrix_local.to_3x3()        # bone frame in armature space (rest) = the FBX node frame
            if child is not None:
                d = bones[child].head_local - b.head_local
            else:
                # a terminal bone continues the direction it arrived from (heden's BoneForwardLocal); Blender's tail
                # is an import artefact on this rig (Epic bones run along X, the importer's tails along Y)
                d = b.head_local - b.parent.head_local
            local = frame.inverted() @ d           # the axis in the bone's own frame
            length = (local.length if child is not None else b.length) * unit + extra
            axis = local.normalized()
            inset = min(radius * 0.7, length * 0.2)
            p0 = axis * inset
            p1 = axis * max(length - radius * 0.7, length * 0.6)
            log("%-12s axis (%.2f, %.2f, %.2f) length %.1f cm radius %.1f" % (name, axis.x, axis.y, axis.z, length, radius))
            shapes += f'''							{{
								_class = "PhysicsShapeCapsule"
								parent_bone = "{name}"
								surface_prop = "flesh"
								collision_tags = "ragdoll"
								radius = {radius:.6f}
								point0 = [ {p0.x:.6f}, {p0.y:.6f}, {p0.z:.6f} ]
								point1 = [ {p1.x:.6f}, {p1.y:.6f}, {p1.z:.6f} ]
							}},
'''
            count += 1
        folders += f'''					{{
						_class = "Folder"
						name = "{folder}"
						children =
						[
{shapes}						]
					}},
'''

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
{folders}				]
			}},
		]
	}}
}}
'''
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", newline="\n") as f:
        f.write(out)
    log("wrote", OUT, "capsules", count)


if __name__ == "__main__":
    main()
