"""Write Assets/CharacterToon/human_fp_arms.vmdl: the six arm/hand meshes of the Synty character on the
full skeleton, every retargeted clip in Assets/Animations/SwordFP as an AnimFile, the three fireball
cast clips heden uses (Attack6begin/loop/end, authored on this same skeleton), the `cast_arm` weight
list the arms animgraph masks the cast with, and the graph hook (animgraphs/human_fp_arms.vanmgrph,
written by gen_fp_arms_animgraph.py).

Every bone of the skeleton gets a BoneMarkup with do_not_discard, exactly like human_base.vmdl: ModelDoc
otherwise drops every bone without skin weights (head, pelvis, prop_r, ...), and FirstPersonArms needs the
head to place the rig on the eye. Same material remaps and 0.3937 scale modifier as the piece models.

    python tools/gen_fp_arms_vmdl.py
"""
import glob, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from fbx_bounds import parse, find

PIECES = [
    ("charactertoon/human_arm_upper_l.fbx", ["SK_HUMN_BASE_01_11AUPL_HU01", "human_arm_upper_l_outline"]),
    ("charactertoon/human_arm_upper_r.fbx", ["SK_HUMN_BASE_01_12AUPR_HU01", "human_arm_upper_r_outline"]),
    ("charactertoon/human_arm_lower_l.fbx", ["SK_HUMN_BASE_01_13ALWL_HU01", "human_arm_lower_l_outline"]),
    ("charactertoon/human_arm_lower_r.fbx", ["SK_HUMN_BASE_01_14ALWR_HU01", "human_arm_lower_r_outline"]),
    ("charactertoon/human_hand_l.fbx", ["SK_HUMN_BASE_01_15HNDL_HU01", "human_hand_l_outline"]),
    ("charactertoon/human_hand_r.fbx", ["SK_HUMN_BASE_01_16HNDR_HU01", "human_hand_r_outline"]),
]
LOOPING = {"anim_idle_breathing", "anim_idle_pose", "anim_walk", "anim_run", "anim_sprint_loop", "anim_jump_loop", "anim_block_loop"}
SKELETON_FBX = os.path.join(ROOT, "Assets", "CharacterToon", "human_torso.fbx")

# heden's fireball: raise (begin), hold (loop), throw (end). Same files human_base.vmdl uses.
CAST_CLIPS = [
    ("Attack6begin", "Animations/Magic/Attacks/Attack6begin.fbx", False),
    ("Attack6loop", "Animations/Magic/Attacks/Attack6loop.fbx", True),
    ("Attack6end", "Animations/Magic/Attacks/Attack6end.fbx", False),
]

# The cast mask: heden's upper_body weights on the spine, the staff (right) arm outright, the sword
# arm and the head at 0 so they keep the sword clip and ride the leaning chest (parent-local blend).
CAST_ARM_WEIGHTS = [
    ("pelvis", 0.0),
    ("spine_01", 0.75),
    ("spine_02", 0.88),
    ("spine_03", 0.96),
    ("clavicle_r", 1.0),
    ("upperarm_r", 1.0),
    ("clavicle_l", 0.0),
    ("neck_01", 0.0),
    ("thigh_l", 0.0),
    ("thigh_r", 0.0),
]
ANIMGRAPH = "animgraphs/human_fp_arms.vanmgrph"


def skeleton_bones(path):
    """Every LimbNode name in the FBX, in file order."""
    _, nodes = parse(path)
    names = []
    for objs in find(nodes, "Objects"):
        for o in objs[2]:
            if o[0] == "Model" and o[1][2] == b"LimbNode":
                names.append(o[1][1].decode("latin1").split("\x00")[0])
    return names


def mesh(filename, names):
    ex = "".join('\t\t\t\t\t\t\t\t"%s",\n' % n for n in names)
    return f'''					{{
						_class = "RenderMeshFile"
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


def anim(name, source, looping):
    return f'''					{{
						_class = "AnimFile"
						name = "{name}"
						activity_name = ""
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


def weightlist(name, weights):
    entries = "".join(f'''							{{
								bone = "{b}"
								weight = {w}
							}},
''' for b, w in weights)
    return f'''					{{
						_class = "WeightList"
						name = "{name}"
						default_weight = 0.0
						weights =
						[
{entries}						]
						master_morph_weight = -1.0
						morph_weights = [  ]
					}},
'''


def main():
    clips = sorted(glob.glob(os.path.join(ROOT, "Assets", "Animations", "SwordFP", "*.fbx")))
    bones = skeleton_bones(SKELETON_FBX)
    anims = "".join(anim(os.path.splitext(os.path.basename(c))[0], "Animations/SwordFP/" + os.path.basename(c),
                         os.path.splitext(os.path.basename(c))[0] in LOOPING) for c in clips)
    anims += "".join(anim(n, s, l) for n, s, l in CAST_CLIPS)
    meshes = "".join(mesh(f, n) for f, n in PIECES)
    markups = "".join(markup(b) for b in bones)
    weightlists = weightlist("cast_arm", CAST_ARM_WEIGHTS)
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
							{{
								from = "outline.vmat"
								to = "shaders/basecharactershader.vmat"
							}},
							{{
								from = "m_basematerial.vmat"
								to = "shaders/basecharacteroutline.vmat"
							}},
						]
						use_global_default = false
						global_default_material = "materials/default.vmat"
					}},
				]
			}},
			{{
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
				default_root_bone_name = ""
			}},
			{{
				_class = "BoneMarkupList"
				children =
				[
{markups}				]
			}},
			{{
				_class = "WeightListList"
				children =
				[
{weightlists}				]
			}},
			{{
				_class = "ModelModifierList"
				children =
				[
					{{
						_class = "ModelModifier_ScaleAndMirror"
						scale = 0.3937
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
    path = os.path.join(ROOT, "Assets", "CharacterToon", "human_fp_arms.vmdl")
    with open(path, "w", newline="\n") as f:
        f.write(out)
    print("wrote", path, "clips", len(clips) + len(CAST_CLIPS), "bones kept", len(bones))


if __name__ == "__main__":
    main()
