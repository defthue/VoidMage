"""Generate voidmage DungeonPack vmdls + the slot vmats, in the heden DungeonPack format.

Input: fbx_all.json (from fbx_scan_all.py) -- per FBX: model node names, node scale, material slots.
Output:
  Assets/Shaders/<slot>.vmat            one per material slot found in the FBX files
  Assets/DungeonPack/<stem>.vmdl        one per FBX (lowercase name, like heden)
"""
import json, os, re, sys

from wood_props import WOOD_MODELS, WOOD_VMAT   # the wooden props go on shaders/woodshader.vmat instead

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SCAN = sys.argv[1]
TEX = "textures/dungeontextures/"

# ---------------------------------------------------------------- vmats
TOON = '''// THIS FILE IS AUTO-GENERATED

Layer0
{{
	shader "shaders/heden_toon.shader"

	//---- Albedo ----
	F_USE_TEXTURE 1

	//---- Toon Lighting ----
	F_USE_RAMP 1

	//---- Albedo ----
	AlbedoTexture "{albedo}"
	g_vTextureTint "[{tint} 0.000000]"

	//---- Debug ----
	g_flDebugMode "0.000"

	//---- Dynamic Lights ----
	g_flDynamicLightBands "4.000"
	g_flDynamicLightBandSoftness "0.080"
	g_flDynamicLightOuterBandScale "{dl_outer_scale}"
	g_flDynamicLightOuterSoftness "{dl_outer_soft}"
	g_flDynamicLightStrength "{dl_strength}"

	//---- Toon Lighting ----
	g_flAmbientFloor "{ambient_floor}"
	g_flAmbientLift "{ambient_lift}"
	g_flBandSoftness "0.005"
	g_flBandThreshold "0.500"
	g_flCrispSunShadows "1.000"
	g_flShadowBandSoftness "0.030"
	g_flShadowNormalOffset "2.000"
	g_flShadowSmoothing "1.500"
	g_flSunColorInfluence "0.400"

	//---- Runestone ---- (the fire rig drives RuneTint / RuneGlow per flame card so it blooms)
	g_flRuneEnable "{rune_enable}"

	//---- Toon Ramp ----
	g_flRampBrightness "1.000"
	g_flRampShadowPoint "0.250"
	ToonRamp "materials/heden_ramp_day.png"

	//---- Style Test ----
	DetailTexture "materials/style_detail/detail_grain.png"
	g_flCrushHuePreserve "0.500"
	g_flCrushLevels "64.000"
	g_flDetailScale "32.000"
	g_flDetailStrength "0.050"
	g_flDetailWobble "0.010"
	g_flDetailWobbleScale "16.000"
}}
'''

OUTLINE = '''// THIS FILE IS AUTO-GENERATED

Layer0
{
	shader "shaders/heden_outline.shader"

	//---- Outline ----
	g_flOutlineMaxWorld "2.000"
	g_flOutlineMinWorld "0.200"
	g_flOutlineScreenWidth "1.000"
	g_flZoneDarknessResponse "0.900"
	g_vOutlineColor "[0.003922 0.003922 0.003922 0.000000]"
}
'''

# Values copied from heden's DungeonShader.vmat (props/walls) and DungeonFloor.vmat (floor):
# the floor runs a much darker ambient so torch light does the work.
PROPS = dict(dl_outer_scale="1.000", dl_outer_soft="0.400", dl_strength="1.000", ambient_floor="0.400", ambient_lift="0.150", rune_enable="0.000")
FLOOR = dict(dl_outer_scale="0.350", dl_outer_soft="0.450", dl_strength="1.500", ambient_floor="0.060", ambient_lift="0.000", rune_enable="0.000")
# the flame cards take the runestone glow path so FireLightSystem can push them overbright (bloom)
FLAME = dict(dl_outer_scale="1.000", dl_outer_soft="0.400", dl_strength="0.000", ambient_floor="1.000", ambient_lift="0.000", rune_enable="1.000")
WHITE = "1.000000 1.000000 1.000000"

VMATS = {
    # slot name in FBX -> (albedo, tint, lighting preset)
    "DungeonShader":   (TEX + "dungeons_texture_01.png",            WHITE, PROPS),
    "DungeonFloor":    (TEX + "dungeons_texture_floortiles_01.png", WHITE, FLOOR),
    "DungeonFloor2":   (TEX + "dungeons_texture_floortile_09_01.png", WHITE, FLOOR),
    "DungeonFloor3":   (TEX + "dungeons_texture_floortile_10_01.png", WHITE, FLOOR),
    "DungeonWallTiles":(TEX + "dungeons_texture_walltiles_01.png",  WHITE, PROPS),
    "DungeonWater":    (TEX + "dungeons_texture_01.png",            "0.700000 0.850000 1.000000", FLOOR),
    "DungeonGlass":    (TEX + "dungeons_texture_01.png",            "0.850000 0.950000 1.000000", PROPS),
    "DungeonFxFlame":  (TEX + "dungeons_texture_01.png",            WHITE, FLAME),
}

# FBX slot name -> vmat the vmdl remaps it to
SLOT_TO_VMAT = {k.lower(): "shaders/%s.vmat" % k.lower() for k in VMATS}
SLOT_TO_VMAT["fantasykingdomoutline"] = "shaders/dungeonoutline.vmat"
SLOT_TO_VMAT["dungeonoutline"] = "shaders/dungeonoutline.vmat"     # re-split weapons name the hull this
SLOT_TO_VMAT["lambert2"] = "shaders/dungeonshader.vmat"          # raw Synty FX meshes

# Weapon slots (sword/staff re-split in Blender, 2026-09): hand-authored vmats, never written here.
# NOTE the two weapon vmdls (sm_wep_ornate_sword_02, sm_wep_staff_gem_01) carry a hand-set
# RenderMeshFile import_scale = 0.2 and the staff remaps gemshader to shaders/staffgem.vmat;
# regenerating them blindly resets both.
SLOT_TO_VMAT["woodhandleshader"] = "shaders/woodhandleshader.vmat"
SLOT_TO_VMAT["ropeshader"] = "shaders/staffshader.vmat"
SLOT_TO_VMAT["brassshader"] = "shaders/brassshader.vmat"
SLOT_TO_VMAT["bladeshader"] = "shaders/bladeshader.vmat"
SLOT_TO_VMAT["gemshader"] = "shaders/gemshader.vmat"

# slots whose meshes must not block the player
NO_PHYSICS_SLOTS = {"dungeonfxflame", "dungeonwater"}

# ---------------------------------------------------------------- vmdl
VMDL = '''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc30:version{{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1}} -->
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
{remaps}
						]
						use_global_default = false
						global_default_material = "materials/default.vmat"
					}},
				]
			}},
{physics}			{{
				_class = "RenderMeshList"
				children =
				[
					{{
						_class = "RenderMeshFile"
						filename = "dungeonpack/{fbx}"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = {scale}
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{{
							exclude_by_default = true
							exception_list =
							[
{render_nodes}
							]
						}}
					}},
				]
			}},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}}
}}'''

PHYSICS = '''			{{
				_class = "PhysicsShapeList"
				children =
				[
					{{
						_class = "PhysicsMeshFile"
						filename = "dungeonpack/{fbx}"
						parent_bone = ""
						surface_prop = "default"
						collision_tags = "solid"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = {scale}
						import_filter =
						{{
							exclude_by_default = true
							exception_list =
							[
{phys_nodes}
							]
						}}
					}},
				]
			}},
'''

REMAP = '''							{{
								from = "{frm}"
								to = "{to}"
							}},'''


def node_list(names):
    return "\n".join('								"%s",' % n for n in names)


def import_scale(stem, entry):
    """cm -> inch is 0.3937 (heden). Nodes carrying their own 100x scale need it divided out;
    the raw FX meshes are in metres."""
    if stem.startswith("FX_"):
        return "39.37"
    scales = {round(m["scale"][0], 4) for m in entry["models"]}
    if scales == {100.0}:
        return "0.003937"
    return "0.3937"


def main():
    scan = json.load(open(SCAN))
    shader_dir = os.path.join(PROJ, "Assets/Shaders")
    pack_dir = os.path.join(PROJ, "Assets/DungeonPack")

    # vmats ------------------------------------------------------------------
    for name, (albedo, tint, preset) in VMATS.items():
        with open(os.path.join(shader_dir, name + ".vmat"), "w", newline="\n") as fh:
            fh.write(TOON.format(albedo=albedo, tint=tint, **preset))
    with open(os.path.join(shader_dir, "DungeonOutline.vmat"), "w", newline="\n") as fh:
        fh.write(OUTLINE)
    print("vmats:", len(VMATS) + 1)

    # vmdls ------------------------------------------------------------------
    written = 0; nophys = []; scaled = []
    unknown_slots = set()
    for fbx, entry in sorted(scan.items()):
        stem = fbx[:-4]
        models = [m["name"] for m in entry["models"]]
        outline = [n for n in models if n.endswith("_Outline")]
        base = [n for n in models if not n.endswith("_Outline")]
        mats = [m for m in entry["materials"]]
        slots = {m.lower() for m in mats}
        remaps = []
        for m in mats:
            key = m.lower()
            if key not in SLOT_TO_VMAT:
                unknown_slots.add(m); continue
            to = SLOT_TO_VMAT[key]
            if key == "dungeonshader" and stem.lower() in WOOD_MODELS:
                to = WOOD_VMAT
            remaps.append(REMAP.format(frm=key + ".vmat", to=to))
        scale = import_scale(stem, entry)
        if scale != "0.3937": scaled.append((stem, scale))
        base_slots = slots - {"fantasykingdomoutline"}
        want_phys = not stem.startswith("FX_") and not (base_slots & NO_PHYSICS_SLOTS)
        if not want_phys: nophys.append(stem)
        physics = PHYSICS.format(fbx=fbx.lower(), scale=scale, phys_nodes=node_list(base)) if want_phys else ""
        body = VMDL.format(remaps="\n".join(remaps), physics=physics, fbx=fbx.lower(), scale=scale,
                           render_nodes=node_list(outline + base))
        with open(os.path.join(pack_dir, stem.lower() + ".vmdl"), "w", newline="\n") as fh:
            fh.write(body)
        written += 1
    print("vmdls:", written)
    print("no physics:", len(nophys), nophys)
    print("non-default import scale:", scaled)
    if unknown_slots:
        print("UNKNOWN SLOTS (no remap written):", unknown_slots)


if __name__ == "__main__":
    main()
