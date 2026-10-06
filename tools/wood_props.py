"""Which DungeonPack models are wood, and the switch that points them at shaders/woodshader.vmat.

Every Synty prop comes in on the one `DungeonShader` slot; the wooden ones get remapped to WoodShader.vmat
(the sword handle's look) instead. This file is the list. Run it to patch the vmdls in place:

    python tools/wood_props.py            # rewrite the remap in Assets/DungeonPack + Assets/Breakables
    python tools/wood_props.py --check    # only report which vmdls would change

gen_dungeon_assets.py and gen_breakable_kits.py import the list so a regeneration keeps the wood.
"""
import os, re, sys

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
WOOD_VMAT = "shaders/woodshader.vmat"
STONE_VMAT = "shaders/dungeonshader.vmat"

# DungeonPack stems (lowercase, no extension). Whole models: a table with iron studs goes wood as one.
WOOD_MODELS = {
    # furniture (the user's wooden-furniture list, 2026-09-07)
    "sm_prop_table_01", "sm_prop_table_round_01", "sm_prop_table_round_02", "sm_prop_table_round_broken_01",
    "sm_prop_stool_01", "sm_prop_stool_02", "sm_prop_stool_03", "sm_prop_bench_01",
    "sm_prop_bed_01", "sm_prop_bed_02",
    "sm_prop_bookcase_01", "sm_prop_bookcase_02", "sm_prop_bookcase_03",
    "sm_prop_coffin_01", "sm_prop_coffin_lid_01", "sm_prop_weaponrack_01",
    "sm_prop_toture_stocks_01", "sm_prop_toture_stretchtable_01",
    # containers (not the iron-bound chests, the metal crates or the ornate crate)
    "sm_prop_barrel_01", "sm_prop_barrel_02", "sm_prop_barrel_03", "sm_prop_barrel_broken_01", "sm_prop_barrel_broken_02",
    "sm_prop_barrel_large_01",
    "sm_prop_crate_wood_01", "sm_prop_crate_wood_02", "sm_prop_crate_wood_03", "sm_prop_crate_wood_04", "sm_prop_crate_wood_05",
    "sm_prop_chest_wood_01", "sm_prop_chest_wood_lid",
    # planks, ladders, logs
    "sm_prop_plank_01", "sm_prop_plank_02", "sm_env_rubble_plank_01", "sm_env_rubble_plank_02", "sm_env_rubble_plank_03",
    "sm_prop_ladder_01",
    "sm_prop_log_spike_01", "sm_prop_log_spike_02", "sm_prop_log_spike_03", "sm_prop_log_spike_04", "sm_prop_log_spike_05",
    "sm_prop_log_spike_06", "sm_prop_log_spike_07", "sm_prop_log_spike_08", "sm_prop_log_spike_09",
    "sm_prop_log_spike_large_01", "sm_prop_log_spike_large_02", "sm_env_spikefence_01", "sm_env_spikefence_02",
    # doors (the leaves; frames and the barred door are stone / iron)
    "sm_env_door_01", "sm_env_door_02", "sm_env_door_03", "sm_env_door_04", "sm_env_door_05",
    "sm_env_door_large_wood_01", "sm_env_door_large_wood_02",
    "sm_env_doordouble_flat_01", "sm_env_doordouble_flat_02", "sm_env_doordouble_round_01", "sm_env_doordouble_round_02",
    "sm_env_trap_hatch_01",
    # structural timber
    "sm_env_wall_wood_beams_01",
    "sm_env_basement_support_beam_01", "sm_env_basement_support_pole_01", "sm_env_basement_support_wall_01",
    "sm_env_basement_wallpanel_01",
    "sm_env_wood_bridge_01", "sm_env_wood_bridge_02", "sm_env_wood_bridge_start_01",
    "sm_env_wood_construction_01", "sm_env_wood_construction_02",
    "sm_env_wood_construction_wheel_01", "sm_env_wood_construction_wheel_02",
    "sm_env_wood_construction_wheel_03", "sm_env_wood_construction_wheel_04",
    "sm_env_wood_platform_01", "sm_env_wood_platform_02", "sm_env_wood_platform_03", "sm_env_wood_platform_04",
    "sm_env_wood_pole_01", "sm_env_wood_pole_02", "sm_env_wood_railing_01", "sm_env_wood_railing_02",
    "sm_env_wood_supports_01", "sm_env_wood_supports_02",
}


def is_wood_kit(kit_id):
    """Breakable kits: every crate, barrel and furniture kit is wood; vase shards and books are not."""
    return not (kit_id.startswith("vase") or kit_id.startswith("book"))


def target_vmat(stem):
    return WOOD_VMAT if stem.lower() in WOOD_MODELS else STONE_VMAT


# woodhandleshader: the pre-WoodShader hand edits (a barrel was pointed at the sword handle's vmat to try the look)
_REMAP = re.compile(r'(from = "dungeonshader\.vmat"\s*\n\s*to = ")shaders/(?:dungeonshader|woodshader|woodhandleshader)\.vmat(")')


def patch(path, wood):
    """Point the DungeonShader slot of one vmdl at the wood or the stone vmat. Returns True when the file changed."""
    text = open(path, encoding="utf-8").read()
    want = WOOD_VMAT if wood else STONE_VMAT
    new, n = _REMAP.subn(lambda m: m.group(1) + want + m.group(2), text)
    if n == 0 or new == text:
        return False
    with open(path, "w", newline="\n", encoding="utf-8") as f:
        f.write(new)
    return True


def main():
    check = "--check" in sys.argv
    pack = os.path.join(PROJ, "Assets", "DungeonPack")
    brk = os.path.join(PROJ, "Assets", "Breakables")
    changed, missing = [], set(WOOD_MODELS)
    for name in sorted(os.listdir(pack)):
        if not name.endswith(".vmdl"):
            continue
        stem = name[:-5]
        missing.discard(stem)
        wood = stem in WOOD_MODELS
        text = open(os.path.join(pack, name), encoding="utf-8").read()
        if 'from = "dungeonshader.vmat"' not in text:
            continue
        # a wood model goes on the wood vmat whatever it had; a non-wood model is only taken OFF the wood vmat
        # (a hand-set vmat on some other model, a shield say, is that model's business)
        if wood:
            need = ('to = "%s"' % WOOD_VMAT) not in text
        else:
            need = ('to = "%s"' % WOOD_VMAT) in text
        if need:
            changed.append(("dungeonpack", stem, "wood" if wood else "stone"))
            if not check:
                patch(os.path.join(pack, name), wood)
    for name in sorted(os.listdir(brk)):
        if not name.endswith(".vmdl"):
            continue
        kit = re.sub(r"_p\d+$", "", name[:-5])
        wood = is_wood_kit(kit)
        text = open(os.path.join(brk, name), encoding="utf-8").read()
        # a Dungeon Realms kit (realms_*) or a gaol kit (df_*/dk_*) has one shader for everything and no wood variant:
        # not this file's business
        if kit.startswith(("realms_", "df_", "dk_")) or 'from = "realmsshader.vmat"' in text or 'from = "darkf' in text:
            continue
        want = WOOD_VMAT if wood else STONE_VMAT
        if 'from = "dungeonshader.vmat"' in text and ('to = "%s"' % want) not in text:
            changed.append(("breakables", name[:-5], "wood" if wood else "stone"))
            if not check:
                patch(os.path.join(brk, name), wood)
    for group in ("dungeonpack", "breakables"):
        rows = [c for c in changed if c[0] == group]
        print("%s: %d vmdls %s" % (group, len(rows), "would change" if check else "changed"))
        for _, stem, mat in rows:
            print("   %s -> %s" % (stem, mat))
    if missing:
        print("WOOD_MODELS without a vmdl:", sorted(missing))


if __name__ == "__main__":
    main()
