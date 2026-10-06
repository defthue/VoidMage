"""Write the vmdls for the toonified POLYGON Fantasy Rivals characters.

  python tools/gen_rivals_chars.py [--manifest tools/out/rivals/chars.json] [--pack rivals]

Reads the manifest tools/toonify_rivals.py wrote next to the character FBX (copied to tools/out/<pack>/
chars.json by the import step) and writes Assets/Characters/<Dir>/<name>.vmdl per character: one
RenderMeshFile node `body` listing every mesh and its `_Outline` shell, the material remaps onto the
pack's vmats (gen_pack_assets.py writes those; FantasyRivalsGhost.vmat is hand-kept), a `do_not_discard`
BoneMarkup for every bone, `default_root_bone_name = root` and ModelModifier_ScaleAndMirror 0.3937 (cm
-> inches, the skeleton NPC convention for a rigged FBX: import_scale stays 1.0).

No clips (unless the manifest entry carries pre-rendered `anims` nodes, as gen_psx_assets.py does for a PSX monster
with clips), no ragdoll, no animgraph: these are models to look at and pick from. The rig is Epic-named
but it is NOT the skeleton donor rig (each character has its own proportions), so the Sword & Shield
clips do not carry over by name; retarget_fpp.py is the route when one becomes an enemy.

Sizes are measured off the exported FBX with fbx_bounds.mesh_world_sizes (node scale and rotation
applied), never off Blender's numbers, and printed so a scale slip is visible without the editor.
"""
import argparse, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import fbx_bounds  # noqa: E402

PACKS = {
    "rivals": dict(
        manifest=os.path.join(ROOT, "tools", "out", "rivals", "chars.json"),
        char_dir=os.path.join(ROOT, "Assets", "Characters", "FantasyRivals"),
        char_rel="characters/fantasyrivals/",
        outline_vmat="fantasyrivalsoutline",
        outline_slot="fantasykingdomoutline",
    ),
    "darkfantasy": dict(
        manifest=os.path.join(ROOT, "tools", "out", "darkfantasy", "chars.json"),
        char_dir=os.path.join(ROOT, "Assets", "Characters", "DarkFantasy"),
        char_rel="characters/darkfantasy/",
        outline_vmat="darkfantasyoutline",
        outline_slot="fantasykingdomoutline",
    ),
    "darkfortress": dict(
        manifest=os.path.join(ROOT, "tools", "out", "darkfortress", "chars.json"),
        char_dir=os.path.join(ROOT, "Assets", "Characters", "DarkFortress"),
        char_rel="characters/darkfortress/",
        outline_vmat="darkfortressoutline",
        outline_slot="fantasykingdomoutline",
    ),
    # the PSX Horror-Fantasy monsters (tools/toonify_psx.py + gen_psx_assets.py, which drives write_vmdl itself with a
    # per-monster `scale` in the manifest entry: the pack has no real-world scale, psx_kinds.py's `size` sets it)
    "psxhorror": dict(
        manifest=os.path.join(ROOT, "tools", "out", "psxhorror", "chars.json"),
        char_dir=os.path.join(ROOT, "Assets", "Characters", "PsxHorror"),
        char_rel="characters/psxhorror/",
        outline_vmat="psxoutline",
        outline_slot="psxoutline",
    ),
}

SCALE = 0.3937


def mesh_node(filename, names):
    ex = "".join('\t\t\t\t\t\t\t\t"%s",\n' % n for n in names)
    return f'''				{{
						_class = "RenderMeshFile"
						name = "body"
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


def markup(bone):
    return f'''				{{
						_class = "BoneMarkup"
						target_bone = "{bone}"
						ignore_Translation = false
						ignore_rotation = false
						do_not_discard = true
					}},
'''


def write_vmdl(cfg, entry):
    stem = os.path.splitext(entry["out"])[0]
    fbx_rel = cfg["char_rel"] + entry["out"].lower()
    fbx_path = os.path.join(cfg["char_dir"], entry["out"])
    names = []
    slots = set()
    for p in entry["parts"]:
        names += [p["name"], p["outline"]]
        slots.update(p["slots"])
    remap_pairs = [(s.lower() + ".vmat", "shaders/%s.vmat" % s.lower()) for s in sorted(slots)]
    remap_pairs.append((cfg["outline_slot"] + ".vmat", "shaders/%s.vmat" % cfg["outline_vmat"]))
    remaps = "".join(f'''						{{
								from = "{a}"
								to = "{b}"
							}},
''' for a, b in remap_pairs)
    root = entry["root_bone"]
    if not isinstance(root, str):
        raise SystemExit("%s: %d root bones %s -- the rig needs one root" % (entry["out"], len(root), root))
    markups = "".join(markup(b) for b in entry["bones"])
    scale = entry.get("scale", SCALE)   # the Synty cm -> inch default, or a size-derived one (the PSX monsters)
    anims = entry.get("anims", "")      # AnimFile nodes, pre-rendered by the caller (gen_psx_assets.py); none by default
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
			{{
				_class = "RenderMeshList"
				children =
				[
{mesh_node(fbx_rel, names)}				]
			}},
			{{
				_class = "AnimationList"
				children =
				[
{anims}				]
				default_root_bone_name = "{root}"
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
						scale = {scale:.6f}
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
    vmdl = os.path.join(cfg["char_dir"], stem.lower() + ".vmdl")
    with open(vmdl, "w", newline="\n") as f:
        f.write(out)
    sizes = fbx_bounds.mesh_world_sizes(fbx_path) if os.path.exists(fbx_path) else {}
    body = sizes.get(entry["parts"][0]["name"])
    desc = "%.0f cm tall (%.0f u)" % (body[1], body[1] * scale) if body else "FBX not in place yet"
    print("  %-24s %2d bones root=%-8s slots=%-40s %s" % (stem.lower() + ".vmdl", len(entry["bones"]), root,
                                                        ",".join(sorted(slots)), desc))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack", default="rivals", choices=sorted(PACKS))
    ap.add_argument("--manifest", default=None)
    args = ap.parse_args()
    cfg = PACKS[args.pack]
    manifest = args.manifest or cfg["manifest"]
    with open(manifest, encoding="utf-8") as f:
        data = json.load(f)
    os.makedirs(cfg["char_dir"], exist_ok=True)
    n = 0
    for src, entry in sorted(data.items()):
        if not entry.get("ok"):
            print("  SKIP %s: %s" % (src, entry.get("error")))
            continue
        write_vmdl(cfg, entry)
        n += 1
    print("%d character vmdls in %s" % (n, os.path.relpath(cfg["char_dir"], ROOT)))


if __name__ == "__main__":
    main()
