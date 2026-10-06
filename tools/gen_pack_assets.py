"""Generate the vmdls + slot vmats for a Synty environment pack, in the voidmage/heden format.

  python tools/fbx_scan_all.py Assets/<Pack> <scan.json>
  python tools/gen_pack_assets.py <pack-name> <scan.json> [--force-vmats]

Packs: "adventure" (POLYGON Adventure), "realms" (POLYGON Dungeon Realms), "rivals" (POLYGON Fantasy
Rivals), "darkfantasy" (POLYGON Dark Fantasy) and "darkfortress" (POLYGON Dark Fortress) -- the last three
static meshes only; their characters get vmdls from gen_synty_chars.py and share these vmats.
vmdls are always rewritten; vmats are only written if absent, so Material Editor tuning
survives a re-run. --force-vmats overrides that.

This is the pack-parameterised sibling of gen_dungeon_assets.py. That script stays as it is:
it is load-bearing for the 797 DungeonPack vmdls, and the sword/staff vmdls under it carry
hand-set values that must never be regenerated.

Output per pack:
  Assets/Shaders/<Slot>.vmat        one per material slot + one outline vmat
  Assets/<PackDir>/<stem>.vmdl      one per FBX, lowercase name

Structure (measured from the shipped packs, do not "improve"):
  * MaterialGroupList remaps every FBX slot name -> shaders/<name>.vmat
  * PhysicsShapeList uses the BASE node only -- including the _Outline shell silently doubles
    every prop's collision triangles
  * RenderMeshList uses both, outline first
  * import_scale 0.3937 (cm -> inches); nodes carrying their own 100x scale need it divided out
"""
import json, os, sys

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

WHITE = "1.000000 1.000000 1.000000"

# Outdoor daylight preset, copied from FantasyKingdomShader.vmat (the proven above-ground look).
# DungeonPack's preset is much darker because torches do the work down there.
OUTDOOR = dict(bands="2.000", band_soft="0.050", dl_outer_scale="1.000", dl_outer_soft="0.000",
               dl_strength="1.000", ambient_floor="0.750", ambient_lift="0.500")

# Underground preset, copied from the SHIPPED Assets/Shaders/DungeonShader.vmat rather than from
# gen_dungeon_assets.py, whose templates have drifted from what is actually in the folder. Dark,
# because torches and forge-glow are what light a dungeon.
INDOOR = dict(bands="4.000", band_soft="0.080", dl_outer_scale="1.000", dl_outer_soft="0.400",
              dl_strength="1.000", ambient_floor="0.100", ambient_lift="0.150")

PACKS = {
    "adventure": dict(
        pack_dir="Assets/AdventurePack",
        prefix="adventurepack/",
        # slot name in FBX -> (albedo, tint, lighting preset)
        # The whole pack is ONE atlas: MaterialList_PolygonAdventure.txt shows all 267 static
        # meshes on PolyAdventureMaterial_01, snow variants included (snow is baked into the UVs).
        vmats={"AdventureShader": ("textures/adventuretextures/polyadventuretexture_01.png", WHITE, OUTDOOR)},
        outline_vmat="AdventureOutline",
        # the toonifier names every shell slot this, pack-wide convention
        outline_slot="fantasykingdomoutline",
    ),
    "realms": dict(
        pack_dir="Assets/DungeonRealms",
        prefix="dungeonrealms/",
        # Essentially one atlas as well: of the 1061 meshes the pack's MaterialList names, 1012
        # are on Mat_01_A. The crystal, gold and spiderweb families want other shaders and are
        # hand-authored in Assets/Shaders (see `specials` below) rather than generated from the
        # toon template, because a gem and a cobweb are not toon-lit surfaces.
        vmats={"RealmsShader": ("textures/dungeonrealmstextures/dungeons_2_texture_01_a.png", WHITE, INDOOR)},
        outline_vmat="RealmsOutline",
        outline_slot="fantasykingdomoutline",
        # written straight through to the vmdl remaps; the vmat files themselves are hand-kept
        specials=["RealmsCrystal", "RealmsGold", "RealmsWeb"],
        # the indoor ink, from the shipped DungeonOutline.vmat
        outline_indoor=True,
        # Synty ships this atlas in twelve recolours on one UV layout. The mossy floors are the
        # same trick: one twin vmat and DungeonBuilder.Theme.cs swaps it per material slot.
        recolours={"RealmsShaderB": "textures/dungeonrealmstextures/dungeons_2_texture_01_b.png"},
    ),
    "rivals": dict(
        pack_dir="Assets/FantasyRivals",
        prefix="fantasyrivals/",
        # The static half of POLYGON Fantasy Rivals (weapons, bases, pouches, harnesses): all 40 on
        # FantasyRivals_Texture_01_A. The characters live in Assets/Characters/FantasyRivals and get
        # their vmdls from gen_rivals_chars.py, but they share these vmats. Indoor preset: they are
        # dungeon bosses and their gear.
        vmats={"FantasyRivalsShader": ("textures/fantasyrivalstextures/fantasyrivals_texture_01_a.png", WHITE, INDOOR)},
        outline_vmat="FantasyRivalsOutline",
        outline_slot="fantasykingdomoutline",
        # the SpiritDemon's glow slot and the Mystic's spectral arms: an alpha gradient, hand-kept
        specials=["FantasyRivalsGhost"],
        outline_indoor=True,
        # Synty ships the one UV layout in 16 palettes (4 textures x A-D). Same trick as the Realms
        # twin: one vmat per palette, swapped per material slot at runtime.
        recolours={"FantasyRivalsShader%s%s" % (t, v): "textures/fantasyrivalstextures/fantasyrivals_texture_%s_%s.png" % (t, v.lower())
                   for t in ("01", "02", "03", "04") for v in ("A", "B", "C", "D") if (t, v) != ("01", "A")},
    ),
    "darkfantasy": dict(
        pack_dir="Assets/DarkFantasy",
        prefix="darkfantasy/",
        # POLYGON Dark Fantasy: the gothic village/crypt kit. One atlas for props, weapons and
        # characters (667 of the MaterialList's slots), and the buildings mix in tiling textures --
        # each of those is the same toon material over its own 2048 diffuse (the pack's normal and
        # metallic maps are not used: heden_toon has no input for them). Indoor preset like Realms;
        # flip to OUTDOOR + --force-vmats if the kit ends up as an overworld village.
        vmats=dict(
            DarkFantasyShader=("textures/darkfantasytextures/polygondarkfantasy_texture_01_a.png", WHITE, INDOOR),
            DarkFantasyBrickSmall01=("textures/darkfantasytextures/brick_small_01.png", WHITE, INDOOR),
            DarkFantasyBrickSmall02=("textures/darkfantasytextures/brick_small_02.png", WHITE, INDOOR),
            DarkFantasyBrickLarge01=("textures/darkfantasytextures/brick_large_01.png", WHITE, INDOOR),
            DarkFantasyBrickLarge02=("textures/darkfantasytextures/brick_large_02.png", WHITE, INDOOR),
            DarkFantasyRoof01=("textures/darkfantasytextures/roof_01.png", WHITE, INDOOR),
            DarkFantasyStone02=("textures/darkfantasytextures/polygondarkfantasy_stone_02.png", WHITE, INDOOR),
            DarkFantasyStuccoBrown01=("textures/darkfantasytextures/stucco_brown_01.png", WHITE, INDOOR),
            DarkFantasyStuccoBrown02=("textures/darkfantasytextures/stucco_brown_02.png", WHITE, INDOOR),
            DarkFantasyStuccoBrown03=("textures/darkfantasytextures/stucco_brown_03.png", WHITE, INDOOR),
            DarkFantasyFloorTiles01=("textures/darkfantasytextures/floor_tiles_01.png", WHITE, INDOOR),
            DarkFantasyWoodFloorBoards01=("textures/darkfantasytextures/wood_floorboards_01_diffuse.png", WHITE, INDOOR),
            DarkFantasyWoodFloorBoards02=("textures/darkfantasytextures/wood_floorboards_02_diffuse.png", WHITE, INDOOR),
            DarkFantasyHouseWall01=("textures/darkfantasytextures/housewall_diffuse.png", WHITE, INDOOR),
            # the window panes: Synty's glass shader over a 1024 pane texture whose alpha never drops
            # under 0.5, so a plain toon surface reads the same at a distance
            DarkFantasyWindow01=("textures/darkfantasytextures/polygondarkfantasy_window_01.png", WHITE, INDOOR),
            DarkFantasyWindow02=("textures/darkfantasytextures/polygondarkfantasy_window_02.png", WHITE, INDOOR),
            DarkFantasyWindow04=("textures/darkfantasytextures/polygondarkfantasy_window_04.png", WHITE, INDOOR),
        ),
        outline_vmat="DarkFantasyOutline",
        outline_slot="fantasykingdomoutline",
        # alpha-cut decal cards (vines, moss, grunge, the divider pattern): hand-authored, not toon surfaces
        specials=["DarkFantasyVines", "DarkFantasyMoss01", "DarkFantasyGrunge01", "DarkFantasyDividerPattern01"],
        outline_indoor=True,
        # eleven palette twins of the atlas
        recolours={"DarkFantasyShader%s%s" % (t, v): "textures/darkfantasytextures/polygondarkfantasy_texture_%s_%s.png" % (t, v.lower())
                   for t in ("01", "02", "03", "04") for v in ("A", "B", "C") if (t, v) != ("01", "A")},
    ),
    "darkfortress": dict(
        pack_dir="Assets/DarkFortress",
        prefix="darkfortress/",
        # POLYGON Dark Fortress: the castle kit (curtain walls, 45-degree pieces, towers, dungeons,
        # siege gear). Atlas + eight tiling textures the walls mix in; the pack's normal maps are not
        # used. Indoor preset like the other two.
        vmats=dict(
            DarkFortressShader=("textures/darkfortresstextures/polygondarkfortress_texture_01_a.png", WHITE, INDOOR),
            DarkFortressBrickLarge01=("textures/darkfortresstextures/brick_large_texture_01.png", WHITE, INDOOR),
            DarkFortressBrickMedium01=("textures/darkfortresstextures/brick_medium_texture_01.png", WHITE, INDOOR),
            DarkFortressBrickSmall01=("textures/darkfortresstextures/brick_small_texture_01.png", WHITE, INDOOR),
            DarkFortressSmoothStone01=("textures/darkfortresstextures/smoothstone_texture_01.png", WHITE, INDOOR),
            DarkFortressSmoothStoneDark01=("textures/darkfortresstextures/smoothstone_dark_texture_01.png", WHITE, INDOOR),
            DarkFortressChippedStone01=("textures/darkfortresstextures/chipped_stone_01.png", WHITE, INDOOR),
            DarkFortressPlanks01=("textures/darkfortresstextures/planks_texture_01.png", WHITE, INDOOR),
        ),
        outline_vmat="DarkFortressOutline",
        outline_slot="fantasykingdomoutline",
        # alpha-cut cards (hand-authored) and the spirit pools on the dungeon's own water material
        specials=["DarkFortressGrunge01", "DarkFortressMoss01", "DarkFortressThorns01", "DarkFortressChains01",
                  "DarkFortressSpiderweb01", "DungeonWater"],
        outline_indoor=True,
        recolours={"DarkFortressShader%s%s" % (t, v): "textures/darkfortresstextures/polygondarkfortress_texture_%s_%s.png" % (t, v.lower())
                   for t in ("01", "02", "03", "04") for v in ("A", "B", "C") if (t, v) != ("01", "A")},
    ),
}

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
	g_flDynamicLightBands "{bands}"
	g_flDynamicLightBandSoftness "{band_soft}"
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
	g_flOutlineScreenWidth "0.800"
	g_flZoneDarknessResponse "1.000"
	g_vOutlineColor "[0.019608 0.019608 0.031373 0.000000]"
}
'''

# The underground ink, copied from the shipped DungeonOutline.vmat: a thicker screen width, a far
# lower world-space floor (0.05 vs 0.2, so the line does not bloat on a wall you stand against),
# near-black, and the interact knobs the dungeon's highlightable props need.
OUTLINE_INDOOR = '''// THIS FILE IS AUTO-GENERATED

Layer0
{
	shader "shaders/heden_outline.shader"

	//---- Outline ----
	g_flInteractBreatheAmount "0.000"
	g_flInteractBreatheHz "1.000"
	g_flInteractThicken "1.500"
	g_flOutlineMaxWorld "2.000"
	g_flOutlineMinWorld "0.050"
	g_flOutlineScreenWidth "1.000"
	g_flZoneDarknessResponse "0.900"
	g_vInteractTint "[0.019608 0.019608 0.031373 0.000000]"
	g_vOutlineColor "[0.003922 0.003922 0.003922 0.000000]"
}
'''

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
						filename = "{prefix}{fbx}"
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
						filename = "{prefix}{fbx}"
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


def import_scale(entry):
    """cm -> inch is 0.3937. Nodes carrying their own 100x scale need it divided out."""
    scales = {round(m["scale"][0], 4) for m in entry["models"]}
    if scales == {100.0}:
        return "0.003937"
    return "0.3937"


def write_vmat(path, body, force):
    """Never clobber a vmat that already exists unless asked: these get tuned by hand in the
    Material Editor afterwards, and silently reverting that is exactly what made
    gen_dungeon_assets.py unrunnable. Borrowed from gen_monster_assets.py."""
    if os.path.exists(path) and not force:
        return False
    with open(path, "w", newline="\n") as fh:
        fh.write(body)
    return True


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    force = "--force-vmats" in sys.argv
    pack_name, scan_path = args[0], args[1]
    cfg = PACKS[pack_name]
    pack_dir = os.path.join(PROJ, cfg["pack_dir"].replace("/", os.sep))
    shader_dir = os.path.join(PROJ, "Assets", "Shaders")
    os.makedirs(pack_dir, exist_ok=True)

    slot_to_vmat = {k.lower(): "shaders/%s.vmat" % k.lower() for k in cfg["vmats"]}
    for name in cfg.get("specials", []):
        slot_to_vmat[name.lower()] = "shaders/%s.vmat" % name.lower()
    # a mesh Synty placed on a palette twin (a Dark Fantasy candelabra on _02_B) names the twin as its slot
    for name in cfg.get("recolours", {}):
        slot_to_vmat[name.lower()] = "shaders/%s.vmat" % name.lower()
    slot_to_vmat[cfg["outline_slot"]] = "shaders/%s.vmat" % cfg["outline_vmat"].lower()

    # vmats ------------------------------------------------------------------
    wrote = kept = 0
    for name, (albedo, tint, preset) in cfg["vmats"].items():
        body = TOON.format(albedo=albedo, tint=tint, **preset)
        if write_vmat(os.path.join(shader_dir, name + ".vmat"), body, force): wrote += 1
        else: kept += 1
    # recolour twins: the same material on another of Synty's atlas variants
    base_preset = next(iter(cfg["vmats"].values()))[2]
    for name, albedo in cfg.get("recolours", {}).items():
        body = TOON.format(albedo=albedo, tint=WHITE, **base_preset)
        if write_vmat(os.path.join(shader_dir, name + ".vmat"), body, force): wrote += 1
        else: kept += 1
    outline_body = OUTLINE_INDOOR if cfg.get("outline_indoor") else OUTLINE
    if write_vmat(os.path.join(shader_dir, cfg["outline_vmat"] + ".vmat"), outline_body, force): wrote += 1
    else: kept += 1
    print("vmats: %d written, %d left alone (use --force-vmats to overwrite)" % (wrote, kept))
    for name in cfg.get("specials", []):
        if not os.path.exists(os.path.join(shader_dir, name + ".vmat")):
            print("  MISSING hand-authored vmat: %s.vmat" % name)

    # vmdls ------------------------------------------------------------------
    scan = json.load(open(scan_path))
    written = 0
    scaled = []
    unknown = set()
    no_outline = []
    for fbx, entry in sorted(scan.items()):
        stem = fbx[:-4]
        models = [m["name"] for m in entry["models"]]
        outline = [n for n in models if n.endswith("_Outline")]
        base = [n for n in models if not n.endswith("_Outline")]
        if not outline:
            no_outline.append(stem)

        remaps = []
        for m in entry["materials"]:
            k = m.lower()
            if k not in slot_to_vmat:
                unknown.add(m)
                continue
            remaps.append(REMAP.format(frm=k + ".vmat", to=slot_to_vmat[k]))

        scale = import_scale(entry)
        if scale != "0.3937":
            scaled.append((stem, scale))

        physics = PHYSICS.format(prefix=cfg["prefix"], fbx=fbx.lower(), scale=scale,
                                 phys_nodes=node_list(base))
        body = VMDL.format(remaps="\n".join(remaps), physics=physics, prefix=cfg["prefix"],
                           fbx=fbx.lower(), scale=scale, render_nodes=node_list(outline + base))
        with open(os.path.join(pack_dir, stem.lower() + ".vmdl"), "w", newline="\n") as fh:
            fh.write(body)
        written += 1

    print("vmdls:", written)
    print("non-default import scale:", scaled)
    if no_outline:
        print("NO OUTLINE MESH:", no_outline)
    if unknown:
        print("UNKNOWN SLOTS (no remap written):", unknown)


if __name__ == "__main__":
    main()
