"""Toonify a Synty POLYGON character/environment pack (Fantasy Rivals, Dark Fantasy) into the voidmage/heden pack format.

  blender --background --factory-startup --python tools/toonify_synty.py -- <pack> <Source_Files> <out_static> <out_chars> [static|chars|all]

  pack = rivals       POLYGON Fantasy Rivals   (20 hero characters, their weapons, a few props)
         darkfantasy  POLYGON Dark Fantasy     (gothic village + crypt kit, 613 static meshes, 15 characters)
         darkfortress POLYGON Dark Fortress    (castle kit, 822 static meshes, 13 characters on the classic rig)

Two kinds of output, the same toon format as toonify_realms.py:

  static  SM_*.fbx  ->  <out_static>/SM_X.fbx          mesh objects SM_X + SM_X_Outline (per mesh in the file)
  chars   rigged FBX ->  <out_chars>/<name>.fbx         armature + every mesh + a <mesh>_Outline shell per mesh,
          and <out_chars>/_chars.json, the manifest gen_synty_chars.py writes the vmdls from (bones, root
          bone, meshes, materials, rest bounds).

  * outline = exact vertex copy with fully-smooth area-weighted normals, NO positional offset, same
    winding, normals NOT flipped -- heden_outline.shader inflates it and culls front faces. On a
    skinned mesh the shell is a duplicate OBJECT (keeps the vertex groups + armature modifier, so it
    deforms with the body), never duplicated faces inside the body mesh. Faces on a `no_outline` slot
    (additive glow cards, alpha-cut vines, window panes) are cut out of the shell: an inflated hull
    behind a flat card is a black card.
  * base custom normals recomputed: 'auto' = fans split at 30 deg, 'smooth' = every face
  * vertices left as-is -> vmdl import_scale 0.3937 (cm -> inches)

MEASURED: both sources are already centimetres (Rivals: SM_Base_Dungeon_01 497 x 496 = a 5 m slab,
SK_Character_Mystic_01 185 tall; Dark Fantasy: SM_Prop_Barrel_01 96 tall, SM_Wep_Sword_01 178 long),
so UNIT_MUL is 1.0 like Adventure and Realms, unlike the DungeonPack's metre-sized source.

Materials: the Maya slot names are junk ("lambert38", "Gothic", "MAT_01A8", "_Ghost2"), so each slot is
looked up in the pack's MaterialList_*.txt by MESH name and SLOT INDEX (the list writes a mesh's slots in
order; 86 Dark Fantasy building pieces carry two to four -- atlas + a tiling brick/roof/stucco/window
texture). The pack's `slot_map` turns Synty's slot name into a vmat family; anything unknown falls back
to the pack's atlas material.

Rigs: both packs' characters are used from their Unreal_Characters folder, the Epic-named rig
(root/pelvis/spine_01, thumb/index/middle, ik_* helpers) every other character in this project uses.
Dark Fantasy's and Fantasy Rivals' rigs are IDENTICAL (0.00 cm / 0.00 deg over all 50 shared bones,
2026-09-11), so clips retargeted for one play on the other; the big Demon/Gargoyle and the Rivals BR
characters have their own limb lengths. None of them is the Dungeon skeleton donor rig (pelvis rest
orientation 180 deg off, limbs 1-5 cm off), so the Sword & Shield clips do not carry over by name --
tools/retarget_fpp.py is the route if one becomes an enemy. Synty exported these rigs' `root` as an FBX
*Root*-type node, which Blender turns into the armature OBJECT: `ensure_root_bone` puts the bone back.
"""
import bpy, bmesh, sys, os, re, json, math, glob, collections

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PACK_NAME, SRC_ROOT, OUT_STATIC, OUT_CHARS = argv[0], argv[1], argv[2], argv[3]
MODE = argv[4] if len(argv) > 4 else "all"

OUTLINE_MAT = "FantasyKingdomOutline"     # pack-wide convention, remapped per pack by the vmdl
UNIT_MUL = 1.0       # both sources are already cm -- see the note above
OFFSET = 0.0         # no baked shell push: the outline shader does the inflation
COS30 = math.cos(math.radians(30.0))


def rivals_static(root):
    skip = re.compile(r'^(Characters|Characters_BR|Medusa_Snakes|Mystic_Arms|FX_)')
    d = os.path.join(root, "FBX")
    return [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.lower().endswith('.fbx') and not skip.match(f)]


def rivals_chars(root):
    d = os.path.join(root, "Characters")
    files = sorted(glob.glob(os.path.join(d, "Unreal_Characters", "*.fbx")))
    # the two classic-rig-only attachments (Medusa's hair, the Mystic's spectral arms) on their own little rigs
    return files + [os.path.join(d, f) for f in ("SK_MedusaSnakes_01.fbx", "SK_Arms_Lower_01.fbx", "SK_Arms_Upper_01.fbx")]


def darkfantasy_static(root):
    """Everything under FBX/ except the Characters subtree, plus the static SM_Chr_Attach_* pieces in
    Characters/Attachments. Skipped: FX_* (3-5 vertex flame cards for Synty's particle system), the two
    Misc meshes (an inverted skybox sphere at 390x node scale and a stray polySurface), the skydome, the
    combined Characters/BigCharacters files, and Capes/Tail_Rig (rigged: the chars batch takes them)."""
    skip = re.compile(r'^(FX_|INVERTED_NORMAL_SPHERE|polySurface66|SM_Env_Skydome|Characters|BigCharacters|Capes|Tail_Rig)')
    fbx = os.path.join(root, "FBX")
    out = []
    for f in sorted(glob.glob(os.path.join(fbx, "**", "*.fbx"), recursive=True)):
        rel = os.path.relpath(f, fbx)
        if rel.startswith("Characters" + os.sep) and not rel.startswith(os.path.join("Characters", "Attachments") + os.sep):
            continue
        if skip.match(os.path.basename(f)):
            continue
        out.append(f)
    names = collections.Counter(os.path.basename(f) for f in out)
    dupes = [n for n, c in names.items() if c > 1]
    if dupes:
        raise SystemExit("static basenames collide across folders: %s" % dupes)
    return out


def darkfantasy_chars(root):
    d = os.path.join(root, "FBX", "Characters")
    files = sorted(glob.glob(os.path.join(d, "Unreal_Characters", "*.fbx")))
    return files + [os.path.join(d, "Attachments", f) for f in ("Capes.fbx", "Tail_Rig_01.fbx")]


def rivals_char_name(stem):
    """SK_BR_Character_Troll_01 -> troll_01, SK_Character_SpiritDemon -> spiritdemon, SK_Arms_Lower_01 -> arms_lower_01."""
    s = re.sub(r'^SK_', '', stem)
    s = re.sub(r'^(BR_)?Character_', '', s)
    return s.lower()


def darkfantasy_char_name(stem):
    """SK_Chr_Skeleton_01 -> skeleton_01, SK_Chr_Attach_Hair_Witch_01 -> attach_hair_witch_01, Capes -> capes."""
    return re.sub(r'^SK_(Chr_)?', '', stem).lower()


def darkfortress_static(root):
    """FBX/ is flat: 828 meshes. Skipped: FX_* (candle glow cards for Synty's particles), the combined
    Characters/BR_Characters files and Cape (rigged: the chars batch takes them), and FBX/Collision (187
    Unity collision hulls -- the vmdl's PhysicsMeshFile comes from the render mesh)."""
    skip = re.compile(r'^(FX_|Characters|BR_Characters|Cape)')
    d = os.path.join(root, "FBX")
    return [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.lower().endswith('.fbx') and not skip.match(f)]


def darkfortress_chars(root):
    """No SK_ files in this pack: every character sits in ONE of two combined files on Synty's classic rig
    (11 characters + 7 rigged attachments in Characters.fbx, the Behemoth and Colossus giants on a bigger
    rig in BR_Characters.fbx), plus the cape on its own rig. `split_chars` exports one FBX per mesh."""
    d = os.path.join(root, "FBX")
    return [os.path.join(d, f) for f in ("Characters.fbx", "BR_Characters.fbx", "Cape.fbx")]


def darkfortress_char_name(stem):
    """SM_Chr_Apothecary_01 -> apothecary_01, SM_Chr_Attach_Hood_01 -> attach_hood_01, Cape -> cape."""
    return re.sub(r'^S[MK]_(Chr_)?', '', stem).lower()


DF = "DarkFantasy"
DK = "DarkFortress"
PACKS = {
    "rivals": dict(
        material_list="MaterialList_PolygonFantasyRivals.txt",
        static_files=rivals_static, char_files=rivals_chars, char_name=rivals_char_name,
        default_mat="FantasyRivalsShader",
        # Synty slot (from the MaterialList) -> vmat family. All 40 static meshes and 19 of the 20
        # characters are on the one atlas; the SpiritDemon's second slot and the Mystic's spectral arms
        # want the translucent look Synty did with a custom shader over the Mystic_Arms.tga alpha
        # gradient -> the hand-kept FantasyRivalsGhost.vmat.
        slot_map={"FantasyRivals_01_A": "FantasyRivalsShader",
                  "FantasyRivals_SpiritDemon": "FantasyRivalsGhost",
                  "FantasyRivals_MysticArms": "FantasyRivalsGhost"},
        # (regex on the Maya material name, family): the SpiritDemon's `_Ghost2` slot
        by_src_mat=[(re.compile(r'^_Ghost'), "FantasyRivalsGhost")],
        # (regex on the mesh node name, family): the arms are "Arms_01" in the list, "SK_Arms_*" in the FBX
        by_mesh=[(re.compile(r'^SK_Arms_'), "FantasyRivalsGhost")],
        smooth_re=re.compile(r'Mushroom|Tree|Bones|Skull|Dirt|Grass|Rock|Snakes|Arms'),
        no_outline=set(),
    ),
    "darkfantasy": dict(
        material_list="MaterialList_PolygonDarkFantasy.txt",
        static_files=darkfantasy_static, char_files=darkfantasy_chars, char_name=darkfantasy_char_name,
        default_mat=DF + "Shader",
        # One atlas (Mat_01_A; _01_B/_02_B are palette twins of the same UVs -- the few meshes Synty put on
        # them get the matching twin vmat, which gen_pack_assets.py writes anyway) plus the tiling textures
        # the buildings mix in. Each tiling family is heden_toon over that texture (gen_pack_assets.py
        # writes them); the vine/moss/grunge/divider cards are hand-kept alpha cutouts.
        slot_map={"PolygonDarkFantasy_Mat_01_A": DF + "Shader", "PolygonDarkFantasy_Mat_01_B": DF + "Shader01B",
                  "PolygonDarkFantasy_Mat_02_B": DF + "Shader02B", "Default-Material": DF + "Shader",
                  "Glass_01": DF + "Shader",
                  "Brick_Small_01": DF + "BrickSmall01", "Brick_Small_02": DF + "BrickSmall02",
                  "Brick_02": DF + "BrickLarge01", "Brick_01": DF + "BrickLarge02",
                  "Roof_01": DF + "Roof01", "Stone_02": DF + "Stone02",
                  "Stucco_Brown_01": DF + "StuccoBrown01", "Stucco_Brown_02": DF + "StuccoBrown02",
                  "Stucco_Brown_03": DF + "StuccoBrown03", "Floor_Tiles_01": DF + "FloorTiles01",
                  "Grunge_01": DF + "Grunge01", "Wood_FloorBoards_01": DF + "WoodFloorBoards01",
                  "Wood_FloorBoards_02": DF + "WoodFloorBoards02", "House_01": DF + "HouseWall01",
                  "DividerPattern_01": DF + "DividerPattern01", "Moss_01": DF + "Moss01",
                  "WindowGlass_01": DF + "Window01", "WindowGlass_02": DF + "Window02", "WindowGlass_04": DF + "Window04",
                  "Vines": DF + "Vines"},
        # ("Additive_Circle_Glow_01", the candle glow discs, only occurs in the skipped FX_ card files)
        by_src_mat=[],
        by_mesh=[],
        smooth_re=re.compile(r'Tree|Rock|Cliff|Basalt|Hill|Boulder|Bones|Skull|Vine|Plant|Grass|Moss|Bush|Mushroom|Root|'
                             r'Landslide|Ocean|Dead|Rotten|Meat|Fish|Bread|Apple|Hair|Cape|Tail|Wing|Web|Cloud|Fog|Mud|Dirt|Ground'),
        # flat cards (alpha-cut vines/moss/grunge/divider decals, window panes): an
        # inflated hull behind them is a black card
        no_outline={DF + "Vines", DF + "Moss01", DF + "DividerPattern01", DF + "Grunge01",
                    DF + "Window01", DF + "Window02", DF + "Window04"},
    ),
    "darkfortress": dict(
        material_list="MaterialList_PolygonDarkFortress.txt",
        static_files=darkfortress_static, char_files=darkfortress_chars, char_name=darkfortress_char_name,
        split_chars=True,
        default_mat=DK + "Shader",
        # The atlas in five palettes Synty actually placed meshes on (the 45-degree floor tiles are on 04_C,
        # 69 slots on 01_B), eight tiling stone/brick/plank textures, six alpha-cut card families, and the
        # spirit pools / water planes on the dungeon's own water material. The MaterialList's fire, ember,
        # blood, portal, lightning and candle-glow slots only occur on FX_ meshes and Unity particle systems.
        # Wind-shader flags/hammock and every glass/potion/jar slot are the atlas.
        slot_map={"PolygonDarkFortress_Mat_01_A": DK + "Shader", "PolygonDarkFortress_Mat_01_B": DK + "Shader01B",
                  "PolygonDarkFortress_Mat_01_C": DK + "Shader01C", "PolygonDarkFortress_Mat_02_B": DK + "Shader02B",
                  "PolygonDarkFortress_Mat_04_B": DK + "Shader04B", "PolygonDarkFortress_Mat_04_C": DK + "Shader04C",
                  "Brick_Large_01": DK + "BrickLarge01", "Brick_Large_Double_01": DK + "BrickLarge01", "brick_large": DK + "BrickLarge01",
                  "Brick_Medium_01": DK + "BrickMedium01", "Brick_Medium_Half_01": DK + "BrickMedium01",
                  "Brick_Small_01": DK + "BrickSmall01", "Brick_Small_Double_01": DK + "BrickSmall01",
                  "SmoothStone_01": DK + "SmoothStone01", "SmoothStone_Dark_01": DK + "SmoothStoneDark01",
                  "Chipped_Stone": DK + "ChippedStone01", "Planks_01": DK + "Planks01",
                  "Grunge_01": DK + "Grunge01", "Moss": DK + "Moss01", "Thorns": DK + "Thorns01",
                  "Chains_01": DK + "Chains01", "Chains_03": DK + "Chains01", "Chains_04": DK + "Chains01",
                  "Basic_Wind_SpiderWebs_01": DK + "Spiderweb01",
                  "Soul_Water_01": "DungeonWater", "Soul_Water_02": "DungeonWater", "Soul_Water_04": "DungeonWater",
                  "Soul_Water_05": "DungeonWater"},
        by_src_mat=[],
        by_mesh=[],
        smooth_re=re.compile(r'Tree|Rock|Cliff|Boulder|Bone|Skull|Thorn|Plant|Grass|Moss|Bush|Mushroom|Root|Dead|Corpse|'
                             r'Rotten|Meat|Fish|Bread|Hair|Cape|Fur|Beard|Web|Cloud|Fog|Mud|Dirt|Ground|Rubble|Spirit|'
                             r'Water|Blood|Pustule|Chain|Rope|Hammock|Flag|Banner|Cloth|Curtain'),
        # flat cards (alpha-cut grunge/moss/thorn/chain/web decals) and the water planes
        no_outline={DK + "Grunge01", DK + "Moss01", DK + "Thorns01", DK + "Chains01", DK + "Spiderweb01", "DungeonWater"},
    ),
}
PACK = PACKS[PACK_NAME]
MATERIAL_LIST = os.path.join(SRC_ROOT, PACK["material_list"])


def load_material_list(path):
    """mesh name -> [Synty slot names in order] (first prefab that lists the mesh wins)."""
    out = {}
    if not os.path.exists(path):
        print("TOON_WARN no MaterialList at %s -- everything falls back to %s" % (path, PACK["default_mat"]))
        return out
    mesh = None
    for line in open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'\s*Mesh Name:\s*(.+)', line)
        if m:
            mesh = m.group(1).strip()
            if mesh in out:
                mesh = None          # keep the first listing
            else:
                out[mesh] = []
            continue
        m = re.match(r'\s*Slot:\s*(\S+)', line)
        if m and mesh:
            out[mesh].append(m.group(1))
    return out


def slot_for(mesh_name, slot_index, src_mat_name, mesh2slots):
    for rx, fam in PACK["by_src_mat"]:
        if src_mat_name and rx.match(src_mat_name):
            return fam
    for rx, fam in PACK["by_mesh"]:
        if rx.match(mesh_name):
            return fam
    keys = (mesh_name, mesh_name + "_01", re.sub(r'^SK_', '', mesh_name), re.sub(r'^SK_(BR_)?', '', mesh_name))
    for k in keys:
        slots = mesh2slots.get(k)
        if slots:
            if slot_index < len(slots):
                return PACK["slot_map"].get(slots[slot_index], PACK["default_mat"])
            if len(slots) == 1:
                return PACK["slot_map"].get(slots[0], PACK["default_mat"])
            return PACK["default_mat"]
    return PACK["default_mat"]


def key(co):
    return (round(co[0], 5), round(co[1], 5), round(co[2], 5))


def compute_normals(me, fan_angle_cos):
    """Per-loop normals. fan_angle_cos=None -> all faces around the vertex position (smooth);
    otherwise faces reachable from the loop's face through edges at this vertex whose dihedral
    cos >= fan_angle_cos (auto). Area-weighted. (toonify_realms.py, unchanged.)"""
    co = [v.co.copy() for v in me.vertices]
    keys = [key(c) for c in co]
    fn = [p.normal.copy() for p in me.polygons]
    fa = [p.area for p in me.polygons]
    pos2f = collections.defaultdict(set)
    edge2f = collections.defaultdict(set)
    pverts = []
    for p in me.polygons:
        vs = [me.loops[l].vertex_index for l in range(p.loop_start, p.loop_start + p.loop_total)]
        pverts.append(vs)
        ks = [keys[v] for v in vs]
        n = len(vs)
        for a in range(n):
            pos2f[ks[a]].add(p.index)
            e = (ks[a], ks[(a + 1) % n]) if ks[a] <= ks[(a + 1) % n] else (ks[(a + 1) % n], ks[a])
            edge2f[e].add(p.index)

    out = [None] * len(me.loops)
    cache = {}
    for p in me.polygons:
        for l in range(p.loop_start, p.loop_start + p.loop_total):
            v = me.loops[l].vertex_index
            k = keys[v]
            if fan_angle_cos is None:
                faces = pos2f[k]
                ck = k
            else:
                ck = (k, p.index)
                if ck in cache:
                    out[l] = cache[ck]
                    continue
                faces = {p.index}
                stack = [p.index]
                around = pos2f[k]
                while stack:
                    f = stack.pop()
                    vs = pverts[f]
                    ks = [keys[x] for x in vs]
                    n = len(vs)
                    for a in range(n):
                        e = (ks[a], ks[(a + 1) % n]) if ks[a] <= ks[(a + 1) % n] else (ks[(a + 1) % n], ks[a])
                        if k not in e:
                            continue
                        for g in edge2f[e]:
                            if g in faces or g not in around:
                                continue
                            if fn[f].dot(fn[g]) >= fan_angle_cos:
                                faces.add(g)
                                stack.append(g)
            if ck in cache:
                out[l] = cache[ck]
                continue
            acc = [0.0, 0.0, 0.0]
            for f in faces:
                acc[0] += fn[f][0] * fa[f]
                acc[1] += fn[f][1] * fa[f]
                acc[2] += fn[f][2] * fa[f]
            ln = math.sqrt(acc[0] ** 2 + acc[1] ** 2 + acc[2] ** 2)
            nrm = (acc[0] / ln, acc[1] / ln, acc[2] / ln) if ln > 1e-12 else tuple(fn[p.index])
            cache[ck] = nrm
            out[l] = nrm
    return out


def vertex_smooth_normals(me):
    """One smooth (all-faces, area-weighted) normal per vertex position, for the shell."""
    keys = [key(v.co) for v in me.vertices]
    acc = collections.defaultdict(lambda: [0.0, 0.0, 0.0])
    for p in me.polygons:
        n = p.normal
        a = p.area
        for l in range(p.loop_start, p.loop_start + p.loop_total):
            k = keys[me.loops[l].vertex_index]
            s = acc[k]
            s[0] += n[0] * a
            s[1] += n[1] * a
            s[2] += n[2] * a
    res = []
    for k in keys:
        s = acc[k]
        ln = math.sqrt(s[0] ** 2 + s[1] ** 2 + s[2] ** 2)
        res.append((s[0] / ln, s[1] / ln, s[2] / ln) if ln > 1e-12 else (0.0, 0.0, 0.0))
    return res


def material_named(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        if m.name != name:          # a stale datablock held the name; steal it
            bpy.data.materials.remove(bpy.data.materials[name])
            m.name = name
    return m


def toonify_mesh(obj, mesh2slots, outline_mat, mode):
    """Rename the slots, build the coincident shell object, recompute the base normals.
    Returns the part record (shell name None when every face sits on a no-outline slot)."""
    me = obj.data
    slots = []
    if not obj.material_slots:
        me.materials.append(material_named(PACK["default_mat"]))
    for i, slot in enumerate(obj.material_slots):
        src_name = slot.material.name if slot.material else ""
        name = slot_for(obj.name, i, src_name, mesh2slots)
        obj.material_slots[i].material = material_named(name)
        slots.append(name)

    if UNIT_MUL != 1.0:
        for v in me.vertices:
            v.co *= UNIT_MUL
    me.update()
    for p in me.polygons:
        p.use_smooth = True

    # --- outline shell BEFORE touching base normals (positions are all it needs) ------------
    vn = vertex_smooth_normals(me)
    shell = obj.copy()                  # keeps parent, vertex groups and the armature modifier
    shell.data = me.copy()
    shell.name = obj.name + "_Outline"  # from the NODE name: the vmdl exception_list keys on it
    shell.data.name = shell.name
    for coll in obj.users_collection:
        coll.objects.link(shell)
    sme = shell.data
    if OFFSET:
        for v, n in zip(sme.vertices, vn):
            v.co.x += n[0] * OFFSET
            v.co.y += n[1] * OFFSET
            v.co.z += n[2] * OFFSET
    # drop the faces that must not get a hull (flat glow / vine / window cards), whole vertices with them
    cut = {i for i, s in enumerate(slots) if s in PACK["no_outline"]}
    if cut:
        keep_vert = [False] * len(sme.vertices)
        bm = bmesh.new()
        bm.from_mesh(sme)
        bm.faces.ensure_lookup_table()
        doomed = [f for f in bm.faces if f.material_index in cut]
        bmesh.ops.delete(bm, geom=doomed, context='FACES')
        bm.to_mesh(sme)
        bm.free()
        sme.update()
        # the shell's smooth normals are keyed by vertex position, recompute on what is left
        vn_shell = vertex_smooth_normals(sme)
    else:
        vn_shell = vn
    if len(sme.polygons) == 0:
        bpy.data.objects.remove(shell, do_unlink=True)
        shell_name = None
    else:
        sme.materials.clear()
        sme.materials.append(outline_mat)
        for p in sme.polygons:
            p.material_index = 0
        sme.normals_split_custom_set([vn_shell[sme.loops[l].vertex_index] for l in range(len(sme.loops))])
        shell_name = shell.name

    # --- base normals ----------------------------------------------------------------------
    me.normals_split_custom_set(compute_normals(me, None if mode == 'smooth' else COS30))
    return {'name': obj.name, 'outline': shell_name, 'polys': len(me.polygons), 'slots': slots,
            'outline_polys': len(sme.polygons) if shell_name else 0}


def world_bounds(obj):
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    return lo, hi


def select_visible(objects):
    """`select_all` skips hidden objects, and Synty ships a few meshes with their FBX Visibility off
    (Dark Fortress' SM_Prop_Shelf_04_Insert_01/02 exported as EMPTY files that way): unhide, then select."""
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.hide_viewport = False
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def export(dst, skinned):
    select_visible([o for o in bpy.context.scene.objects if o.type in ('MESH', 'ARMATURE')])
    if skinned:
        # prep_skeleton_npc.py's proven settings for a rigged character (vmdl: ScaleAndMirror 0.3937)
        bpy.ops.export_scene.fbx(
            filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"},
            add_leaf_bones=False, armature_nodetype="NULL", use_armature_deform_only=False,
            apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
            use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP", use_custom_props=False)
    else:
        bpy.ops.export_scene.fbx(
            filepath=dst, use_selection=True,
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
            axis_forward='-Z', axis_up='Y', bake_space_transform=False,
            mesh_smooth_type='FACE', use_custom_props=False, use_mesh_modifiers=False,
            add_leaf_bones=False, path_mode='AUTO')


def toonify_static(src, dst, mesh2slots):
    base = os.path.splitext(os.path.basename(src))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, use_custom_normals=True)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        return {'ok': False, 'error': 'no meshes'}
    mode = 'smooth' if PACK["smooth_re"].search(base) else 'auto'
    outline_mat = material_named(OUTLINE_MAT)
    parts = [toonify_mesh(obj, mesh2slots, outline_mat, mode) for obj in list(meshes)]
    export(dst, skinned=False)
    slots = sorted({s for p in parts for s in p['slots']})
    return {'ok': True, 'mode': mode, 'slot': slots[0] if len(slots) == 1 else slots, 'parts': parts}


def ensure_root_bone(arm):
    """Synty exported these rigs' `root` as an FBX *Root*-type node (the Dungeon skeletons have it as a
    LimbNode), and Blender turns a Root node into the armature OBJECT, so the imported rig has no root
    bone and three parentless bones (pelvis, ik_foot_root, ik_hand_root). Put it back: an identity bone
    at the armature origin (+Y tail, roll 0 = no rotation in armature space, exactly what the source node
    was) with every former root parented under it. The bone takes the source node's spelling (`root` on
    the Epic rigs, `Root` on Synty's classic rig). Returns True if a bone was added."""
    if any(b.name.lower() == 'root' for b in arm.data.bones):
        return False
    name = arm.name if arm.name.lower() == 'root' else 'root'
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    root = eb.new(name)
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, 10.0, 0.0)
    root.roll = 0.0
    for b in eb:
        if b.parent is None and b.name != name:
            b.parent = root
            b.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    if arm.name.lower() == 'root':        # the object took the node's name; keep it off the bone's
        arm.name = 'Armature'
    return True


def export_selection(dst, objects):
    select_visible(objects)
    bpy.ops.export_scene.fbx(
        filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, armature_nodetype="NULL", use_armature_deform_only=False,
        apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP", use_custom_props=False)


def toonify_char(src, dst, mesh2slots):
    base = os.path.splitext(os.path.basename(src))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, use_custom_normals=True)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(arms) != 1 or not meshes:
        return {'ok': False, 'error': 'expected one armature and meshes, got %d armatures, %d meshes'
                % (len(arms), len(meshes))}
    arm = arms[0]
    # leave the rig in its bind pose: an assigned action would leak a frame into the export
    if arm.animation_data:
        arm.animation_data.action = None
    for o in meshes:
        if o.animation_data:
            o.animation_data.action = None
    bpy.context.scene.frame_set(bpy.context.scene.frame_start)
    bpy.context.view_layer.update()
    added_root = ensure_root_bone(arm)

    outline_mat = material_named(OUTLINE_MAT)
    parts, shells, bounds, modes = [], [], [], []
    for obj in list(meshes):
        if not any(m.type == 'ARMATURE' for m in obj.modifiers):
            return {'ok': False, 'error': '%s carries no armature modifier' % obj.name}
        mode = 'smooth' if PACK["smooth_re"].search(obj.name if PACK.get("split_chars") else base) else 'auto'
        modes.append(mode)
        bounds.append(world_bounds(obj))
        parts.append(toonify_mesh(obj, mesh2slots, outline_mat, mode))
        shells.append(bpy.data.objects.get(parts[-1]['outline']) if parts[-1]['outline'] else None)
    bones = [b.name for b in arm.data.bones]
    roots = [b.name for b in arm.data.bones if b.parent is None]
    common = {'armature': arm.name, 'bones': bones, 'root_bone': roots[0] if len(roots) == 1 else roots,
              'root_added': added_root, 'armature_scale': [round(s, 4) for s in arm.scale]}

    def record(ps, lo, hi, mode):
        slots = sorted({s for p in ps for s in p['slots']})
        return dict(common, ok=True, mode=mode, slot=slots[0] if len(slots) == 1 else slots, parts=ps,
                    # Blender world units (metres here: the cm FBX imports under a 0.01 object scale)
                    rest_min_m=[round(v, 4) for v in lo], rest_max_m=[round(v, 4) for v in hi])

    if PACK.get("split_chars"):
        # one FBX per mesh, each carrying the whole armature: the combined Synty file becomes N models
        out = []
        for obj, shell, part, (lo, hi), mode in zip(meshes, shells, parts, bounds, modes):
            name = PACK["char_name"](obj.name) + ".fbx"
            export_selection(os.path.join(os.path.dirname(dst), name), [arm, obj] + ([shell] if shell else []))
            r = record([part], lo, hi, mode)
            r['out'] = name
            out.append(r)
        return out
    lo = [min(b[0][i] for b in bounds) for i in range(3)]
    hi = [max(b[1][i] for b in bounds) for i in range(3)]
    export(dst, skinned=True)
    return record(parts, lo, hi, modes[0])


def run_batch(files, out_dir, results_name, fn, name_of):
    os.makedirs(out_dir, exist_ok=True)
    results_path = os.path.join(out_dir, results_name)
    results = {}
    if os.path.exists(results_path):
        try:
            results = json.load(open(results_path, encoding='utf-8'))
        except Exception:
            results = {}
    done = fail = 0
    used = collections.Counter()
    for i, src in enumerate(files):
        f = os.path.basename(src)
        if results.get(f, {}).get('ok') or any(v.get('ok') and v.get('src_file') == f for v in results.values()):
            continue
        dst = os.path.join(out_dir, name_of(f))
        try:
            r = fn(src, dst, MESH2SLOTS)
        except Exception as e:
            r = {'ok': False, 'error': repr(e)[:300]}
        # a split character file yields one record per mesh, keyed by its output name
        for rec in (r if isinstance(r, list) else [r]):
            rec.setdefault('out', os.path.basename(dst))
            rec['src'] = os.path.relpath(src, SRC_ROOT)
            rec['src_file'] = f
            results[rec['out'] if isinstance(r, list) else f] = rec
            if rec.get('ok'):
                done += 1
                for p in rec['parts']:
                    for s in p['slots']:
                        used[s] += 1
            else:
                fail += 1
                print("TOON_FAIL %s :: %s" % (f, rec.get('error')))
        failed = any(not rec.get('ok') for rec in (r if isinstance(r, list) else [r]))
        if (i + 1) % 25 == 0 or i + 1 == len(files) or failed:
            json.dump(results, open(results_path, 'w', encoding='utf-8'), indent=0)
            print("TOON_PROGRESS %d/%d ok=%d fail=%d" % (i + 1, len(files), done, fail))
            sys.stdout.flush()
    json.dump(results, open(results_path, 'w', encoding='utf-8'), indent=0)
    print("TOON_SLOTS " + json.dumps(dict(used)))
    print("TOON_DONE %s ok=%d fail=%d total=%d" % (results_name, done, fail, len(files)))


MESH2SLOTS = load_material_list(MATERIAL_LIST)
print("TOON_INFO MaterialList: %d meshes" % len(MESH2SLOTS))

if MODE in ("static", "all"):
    run_batch(PACK["static_files"](SRC_ROOT), OUT_STATIC, "_results.json", toonify_static, lambda f: f)

if MODE in ("chars", "all"):
    run_batch(PACK["char_files"](SRC_ROOT), OUT_CHARS, "_chars.json", toonify_char,
              lambda f: PACK["char_name"](f[:-4]) + ".fbx")
