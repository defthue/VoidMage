"""Write one skeleton kind's vmdl (tools/skeleton_kinds.py; Assets/Characters/SkeletonSlave/skeleton_slave_01.vmdl,
.../SkeletonSoldier/skeleton_soldier_01.vmdl, ...): the toonified Dungeon skeleton character
(tools/prep_skeleton_npc.py) with its swords and shields as body groups and every retargeted Sword & Shield
clip in Assets/Animations/SkeletonSS as an AnimFile (the clips are shared: every kind is the same rig).

Per clip: `looping` from the name (idle / loops / directional walk-jog cycles / run), and an ExtractMotion
child (tx, ty on bone `root`) whenever the clip's root bone actually travels (> 1 cm, read from the FBX with
tools/fbx_anim.py), so the pack's root motion becomes engine root motion instead of a model walking away.
Every bone gets a BoneMarkup with do_not_discard (ModelDoc drops unskinned bones otherwise, and the sockets
only carry the weapons). Same 0.3937 scale modifier as every other cm model here.

    python tools/gen_skeleton_npc_vmdl.py --kind soldier_01 [--filter "idle|walk_f|attack_a_01"]
"""
import argparse, glob, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from fbx_bounds import parse, find  # noqa: E402
from fbx_anim import root_travel  # noqa: E402
import skeleton_kinds  # noqa: E402

# the clip set is per kind (`clip_dir`, a folder under Assets/Animations): SkeletonSS = the Sword & Shield pack
# retargeted from Manny (retarget_fpp.py), SkeletonSword = Synty's own Sword Animations pack retargeted from the
# classic rig (retarget_synty.py); both are authored on the donor rig, so any kind can list either
DEFAULT_CLIP_DIR = "SkeletonSS"

# Body groups `sword` and `shield`: one choice per weapon in the kind's list order (SkeletonBody sets the choice
# from the synced SkeletonNpc.SwordIndex / ShieldIndex), then an empty choice. All hang on weapon_r / shield_l.
# The ragdoll capsules (heden's goblin layout) live in a vmdl_prefab written by gen_skeleton_ragdoll.py; joints
# are built at runtime by SkeletonCorpse.
REMAPS = [("dungeonshader.vmat", "shaders/dungeonshader.vmat"), ("dungeonoutline.vmat", "shaders/dungeonoutline.vmat"),
          # the shields' own material identities (prep_skeleton_npc.py SHIELD_MATERIALS): SkeletonBody flashes exactly
          # those slots blue when a hit lands on the raised shield
          ("dungeonshadershield.vmat", "shaders/dungeonshader_shield.vmat"),
          ("shieldironshader.vmat", "shaders/shieldironshader.vmat"), ("shieldwoodshader.vmat", "shaders/shieldwoodshader.vmat"),
          # the swords' slots (prep_skeleton_npc.py SWORD_MATERIALS): the players' sword shaders for handle, blade and
          # brass trim, the skeletons' own darker iron for crossguard + pommel where the players' sword has brass
          ("woodhandleshader.vmat", "shaders/woodhandleshader.vmat"), ("bladeshader.vmat", "shaders/bladeshader.vmat"),
          ("brassshader.vmat", "shaders/brassshader.vmat"), ("ironshader.vmat", "shaders/ironshader.vmat"),
          # the Realms skeletons' body atlas and their FantasyKingdom swords (one atlas slot each, no split);
          # rows a kind does not use are harmless, this list is a superset for every kind
          ("realmsshader.vmat", "shaders/realmsshader.vmat"),
          ("realmsshadershield.vmat", "shaders/realmsshadershield.vmat"),   # a Realms shield's block-flash identity
          ("fantasykingdomshader.vmat", "shaders/fantasykingdomshader.vmat"),
          # the Dark Fantasy skeletons' body atlas (+ their helmets) and the Dark Fortress corpse's
          ("darkfantasyshader.vmat", "shaders/darkfantasyshader.vmat"),
          ("darkfortressshader.vmat", "shaders/darkfortressshader.vmat"),
          # the Fantasy Rivals atlas and Synty's translucent ghost slot (the SpiritDemon)
          ("fantasyrivalsshader.vmat", "shaders/fantasyrivalsshader.vmat"),
          ("fantasyrivalsghost.vmat", "shaders/fantasyrivalsghost.vmat")]

LOOPING = [re.compile(p) for p in (
    r"^idle$",
    r"(^|_)loop(_|$)",
    r"^(guard_)?(walk|jog)_(f|b|l|r|bl|br)$",
    r"^run$",
    # the Sword Animations pack (SkeletonSword): idle_base, idle_base_sheathed, idle_sheathed_sword_femn/masc,
    # idle_menacing01(_rootmotion); the fidgets, begin/end and transition clips are one-shots
    r"^idle_base(_sheathed)?$",
    r"^idle_sheathed_sword_(femn|masc)$",
    r"^idle_menacing01(_rootmotion)?$",
)]
ROOT_TRAVEL_THRESHOLD_CM = 1.0
ANIMGRAPH = skeleton_kinds.ANIMGRAPH  # written by gen_skeleton_animgraph.py, shared by every kind


def skeleton_bones(path):
    _, nodes = parse(path)
    names = []
    for objs in find(nodes, "Objects"):
        for o in objs[2]:
            if o[0] == "Model" and o[1][2] == b"LimbNode":
                names.append(o[1][1].decode("latin1").split("\x00")[0])
    return names


# the Sword Animations knockdown / stun "loop" clips play ONCE inside the swordsman graph's hurt chain (down -> lie ->
# get up steps on `finished`, which a looping sequence never reports)
PLAY_ONCE = {"knockdown_loop", "stun_loop"}


def is_looping(name):
    return name not in PLAY_ONCE and any(p.search(name) for p in LOOPING)


def mesh(filename, names, node_name):
    """One RenderMeshFile = one compiled render mesh holding every FBX object in `names`. Its `name` is what body
    group choices refer to: the compiled model keeps ONE mesh per node and none of the FBX object names, so choices
    listing FBX object names match nothing and every mesh ends up in every group (both swords drawn at once)."""
    ex = "".join('\t\t\t\t\t\t\t\t"%s",\n' % n for n in names)
    return f'''					{{
						_class = "RenderMeshFile"
						name = "{node_name}"
						filename = "{filename}"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = 1.0
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{{
							exclude_by_default = true
							exception_list =
							[
{ex}							]
						}}
					}},
'''


def extract_motion():
    return '''						children =
						[
							{
								_class = "ExtractMotion"
								extract_tx = true
								extract_ty = true
								extract_tz = false
								extract_rz = false
								linear = false
								quadratic = false
								root_bone_name = "root"
								motion_type = "Single"
							},
						]
'''


def anim(name, source, looping, root_motion):
    return f'''					{{
						_class = "AnimFile"
						name = "{name}"
{extract_motion() if root_motion else ""}						activity_name = ""
						activity_weight = 1
						weight_list_name = ""
						fade_in_time = 0.2
						fade_out_time = 0.2
						looping = {"true" if looping else "false"}
						delta = false
						worldSpace = false
						hidden = false
						anim_markup_ordered = false
						disable_compression = false
						disable_interpolation = false
						enable_scale = false
						source_filename = "{source}"
						start_frame = -1
						end_frame = -1
						framerate = -1.0
						take = 0
						reverse = false
					}},
'''


def markup(bone):
    return f'''					{{
						_class = "BoneMarkup"
						target_bone = "{bone}"
						ignore_Translation = false
						ignore_rotation = false
						do_not_discard = true
					}},
'''


def bodygroup(name, *choices):
    """One choice per render-mesh node name (or list of them), in order, then an empty (hidden) choice last."""
    out = ""
    for meshes in choices:
        if isinstance(meshes, str):
            meshes = [meshes]
        shown = "".join('\t\t\t\t\t\t\t\t\t\t"%s",\n' % m for m in meshes)
        out += f'''						{{
							_class = "BodyGroupChoice"
							meshes =
							[
{shown}							]
						}},
'''
    return f'''					{{
						_class = "BodyGroup"
						name = "{name}"
						children =
						[
{out}							{{
								_class = "BodyGroupChoice"
								meshes = [  ]
							}},
						]
						hidden_in_tools = false
					}},
'''


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--filter", default="", help="regex; only clips whose name matches are listed")
    skeleton_kinds.add_kind_arg(ap)
    args = ap.parse_args()
    kind = skeleton_kinds.kind(args.kind)
    SKELETON_FBX, VMDL_PATH, FBX_REL, PHYSICS_PREFAB = kind["fbx"], kind["vmdl"], kind["fbx_rel"], kind["physics_rel"]
    ANIMGRAPH = kind["animgraph"]
    BODY_MESH = kind["body_mesh"]
    sword_sets = [[w["mesh"], w["mesh"] + "_Outline"] for w in kind["swords"]]
    shield_sets = [[w["mesh"], w["mesh"] + "_Outline"] for w in kind["shields"]]
    # attachments (the knight's helmet) ride the body node: always drawn, never a body-group choice
    body_set = [BODY_MESH] + [n for a in kind.get("attachments", []) for n in (a["mesh"], a["mesh"] + "_Outline")]
    # one folder (`clip_dir`) or several with a name filter each (`clip_dirs`: the swordsman lists every Sword
    # Animations clip plus the Sword & Shield locomotion); a clip name must be unique across them
    clip_sets = kind.get("clip_dirs") or [(kind.get("clip_dir", DEFAULT_CLIP_DIR), None)]
    clips, seen = [], {}
    for folder, pattern in clip_sets:
        rx = re.compile(pattern) if pattern else None
        found = 0
        for c in sorted(glob.glob(os.path.join(ROOT, "Assets", "Animations", folder, "*.fbx"))):
            name = os.path.splitext(os.path.basename(c))[0]
            if rx and not rx.search(name):
                continue
            if name in seen:
                raise SystemExit("clip %s is in both %s and %s" % (name, seen[name], folder))
            seen[name] = folder
            clips.append((name, c, "Animations/%s/%s" % (folder, os.path.basename(c))))
            found += 1
        if not found:
            raise SystemExit("no clips in Animations/%s matching %r" % (folder, pattern))
    if args.filter:
        rx = re.compile(args.filter)
        clips = [c for c in clips if rx.search(c[0])]
    if not clips:
        raise SystemExit("no clips left after --filter")
    CLIP_REL = ", ".join(f for f, _ in clip_sets)
    bones = skeleton_bones(SKELETON_FBX)
    anims = ""
    n_root = n_loop = 0
    for name, c, rel in clips:
        travel, moving = root_travel(c, "root", ROOT_TRAVEL_THRESHOLD_CM)
        looping = is_looping(name)
        n_root += moving; n_loop += looping
        anims += anim(name, rel, looping, moving)
    remaps = "".join(f'''							{{
								from = "{a}"
								to = "{b}"
							}},
''' for a, b in REMAPS)
    # render-mesh node names: body, sword_0.., shield_0..; the body groups list those
    sword_nodes = ["sword_%d" % i for i in range(len(sword_sets))]
    shield_nodes = ["shield_%d" % i for i in range(len(shield_sets))]
    meshes = mesh(FBX_REL, body_set, "body") \
        + "".join(mesh(FBX_REL, s, n) for s, n in zip(sword_sets, sword_nodes)) \
        + "".join(mesh(FBX_REL, s, n) for s, n in zip(shield_sets, shield_nodes))
    markups = "".join(markup(b) for b in bones)
    groups = (bodygroup("sword", *sword_nodes) if sword_nodes else "") + (bodygroup("shield", *shield_nodes) if shield_nodes else "")
    # a kind without a ragdoll yet (tormented_soul) gets no PhysicsShapeList rather than a dangling prefab reference
    physics = f'''			{{
				_class = "PhysicsShapeList"
				children =
				[
					{{
						_class = "Prefab"
						target_file = "{PHYSICS_PREFAB}"
					}},
				]
			}},
''' if os.path.exists(kind["physics_prefab"]) else ""
    if not physics:
        print("no ragdoll prefab at", kind["physics_prefab"], "- PhysicsShapeList omitted")
    out = f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc30:version{{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1}} -->
{{
	rootNode =
	{{
		_class = "RootNode"
		children =
		[
			{{
				_class = "MaterialGroupList"
				children =
				[
					{{
						_class = "DefaultMaterialGroup"
						remaps =
						[
{remaps}						]
						use_global_default = false
						global_default_material = "materials/default.vmat"
					}},
				]
			}},
{physics}			{{
				_class = "RenderMeshList"
				children =
				[
{meshes}				]
			}},
			{{
				_class = "AnimationList"
				children =
				[
{anims}				]
				default_root_bone_name = "root"
			}},
			{{
				_class = "BoneMarkupList"
				children =
				[
{markups}				]
			}},
			{{
				_class = "BodyGroupList"
				children =
				[
{groups}				]
			}},
			{{
				_class = "ModelModifierList"
				children =
				[
					{{
						_class = "ModelModifier_ScaleAndMirror"
						scale = {0.3937 * kind.get("model_scale", 1.0):.6f}
						mirror_x = false
						mirror_y = false
						mirror_z = false
						flip_bone_forward = false
						swap_left_and_right_bones = false
					}},
				]
			}},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = "{ANIMGRAPH}"
		base_model_name = ""
	}}
}}
'''
    with open(VMDL_PATH, "w", newline="\n") as f:
        f.write(out)
    print("wrote", VMDL_PATH, "clips", len(clips), "from", CLIP_REL, "root-motion", n_root, "looping", n_loop, "bones kept", len(bones),
          "swords", [s[0] for s in sword_sets], "shields", [s[0] for s in shield_sets])


if __name__ == "__main__":
    main()
