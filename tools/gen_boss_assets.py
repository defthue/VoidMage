"""Write the Bone Colossus's model assets under Assets/Boss:

    boss/hand_r.vmdl, boss/hand_l.vmdl   the rigged hands (tools/rig_hell_hand.py output): skinned render mesh,
                                         the six pose clips, every bone kept, the skeleton characters' scale
                                         convention (import_scale 1.0 + ModelModifier_ScaleAndMirror 0.3937,
                                         cm mesh under a unit rig node), animgraphs/boss_hand.vanmgrph
No physics: the boss parts get code-made colliders (Code/Boss), never a PhysicsMeshFile (static concave, and
these move). The skull and jaw use the Realms vmdls as they are (dungeonrealms/sm_env_hell_bone_skull_01,
_skull_jaw_01): their physics mesh is ignored without a ModelCollider.

    python tools/gen_boss_assets.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Boss")
ANIMGRAPH = "animgraphs/boss_hand.vanmgrph"
REMAPS = [("realmsshader.vmat", "shaders/realmsshader.vmat"), ("fantasykingdomoutline.vmat", "shaders/realmsoutline.vmat")]


def anim(name, source, looping):
    return f'''				{{
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
    return f'''				{{
					_class = "BoneMarkup"
					target_bone = "{bone}"
					ignore_Translation = false
					ignore_rotation = false
					do_not_discard = true
				}},
'''


def hand(side):
    rig = json.load(open(os.path.join(OUT, "hand_%s_rig.json" % side)))
    stem = rig["source"]
    remaps = "".join(f'''					{{
						from = "{a}"
						to = "{b}"
					}},
''' for a, b in REMAPS)
    anims = "".join(anim(c, "Boss/Animations/hand_%s/%s.fbx" % (side, c), v["looping"]) for c, v in rig["clips"].items())
    markups = "".join(markup(b) for b in rig["bones"])
    return f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc30:version{{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1}} -->
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
			{{
				_class = "RenderMeshList"
				children =
				[
					{{
						_class = "RenderMeshFile"
						name = "hand"
						filename = "boss/hand_{side}.fbx"
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
								"{stem}_Outline",
								"{stem}",
							]
						}}
					}},
				]
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


def forearm(side):
    """The forearm split off at the wrist (tools/rig_hell_hand.py): a static mesh in the same cm-under-a-unit-node
    convention, origin at the wrist, no bones, no physics."""
    rig = json.load(open(os.path.join(OUT, "hand_%s_rig.json" % side)))
    stem = rig["source"]
    remaps = "".join(f'''					{{
						from = "{a}"
						to = "{b}"
					}},
''' for a, b in REMAPS)
    return f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc30:version{{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1}} -->
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
			{{
				_class = "RenderMeshList"
				children =
				[
					{{
						_class = "RenderMeshFile"
						name = "forearm"
						filename = "boss/hand_{side}_forearm.fbx"
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
								"{stem}_Forearm_Outline",
								"{stem}_Forearm",
							]
						}}
					}},
				]
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
		anim_graph_name = ""
		base_model_name = ""
	}}
}}
'''


def main():
    for side in ("r", "l"):
        path = os.path.join(OUT, "hand_%s.vmdl" % side)
        with open(path, "w", newline="\n") as f:
            f.write(hand(side))
        print("wrote", path)
        path = os.path.join(OUT, "hand_%s_forearm.vmdl" % side)
        with open(path, "w", newline="\n") as f:
            f.write(forearm(side))
        print("wrote", path)


if __name__ == "__main__":
    main()
