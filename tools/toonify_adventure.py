"""Toonify the Synty POLYGON Adventure pack into the voidmage/heden pack format.

  blender --background --python tools/toonify_adventure.py -- <src_fbx_dir> <out_dir> [start] [count]

Same measured format the DungeonPack files are in (see toonify_dungeon.py, which produced them):
  * two mesh objects per file: SM_X (base) + SM_X_Outline (shell)
  * base slot renamed to "AdventureShader", outline slot literally "FantasyKingdomOutline"
    (pack-wide convention across every Synty pack here, remapped per pack in the vmdl)
  * outline = exact vertex copy carrying the fully-smooth area-weighted normals, NO positional
    offset, same winding, normals NOT flipped. The shell sits coincident with the base;
    heden_outline.shader is what inflates it (screen-constant width, clamped between
    g_flOutlineMinWorld/MaxWorld) and culls front faces. Baking an offset in here would just
    add a fixed world-space thickness on top of the shader's, so it stays 0.
  * base custom normals recomputed: 'auto' = smoothing fans split at 30 deg dihedral,
    'smooth' = every face around the vertex; both area-weighted
  * vertices left as-is -> vmdl import_scale 0.3937 (cm -> inches)

UNIT_MUL differs per pack -- measure, never assume. The Dungeon pack's Synty source is
metre-sized (SM_Env_Wall_01 is 5.095 x 5.151 units) so toonify_dungeon.py scales it x100 to
reach the cm the pack format wants. The Adventure source is ALREADY cm (SM_Bld_Fence_01 is
311 x 124 units = a 3.1 m fence), so it must not be scaled at all. Check a known piece with
tools/fbx_bounds.py against Assets/DungeonPack/SM_Env_Wall_01.fbx (509 x 515) before running
this on a new pack.

Difference from the dungeon pass: Adventure is a SINGLE-material pack. All 267 static meshes use
PolyAdventureMaterial_01 (PolyAdventureTexture_01) per MaterialList_PolygonAdventure.txt -- the
snow variants included, snow is baked into the atlas -- so there is no slot rule table.
"""
import bpy, sys, os, re, json, math, shutil, collections

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC, OUT = argv[0], argv[1]
START = int(argv[2]) if len(argv) > 2 else 0
COUNT = int(argv[3]) if len(argv) > 3 else 10 ** 9

# TOON_BASE_MAT overrides the slot name for a one-off from another single-atlas pack in the same format
# (FantasyKingdomShader for a FantasyKingdom prop heden never toonified, e.g. SM_Wep_Sword_03)
BASE_MAT = os.environ.get("TOON_BASE_MAT", "AdventureShader")
OUTLINE_MAT = "FantasyKingdomOutline"
UNIT_MUL = 1.0       # Adventure's Synty source is already cm -- see the note above
OFFSET = 0.0         # no baked shell push: the outline shader does the inflation
COS30 = math.cos(math.radians(30.0))

# Organic pieces read better fully smooth; hard-surface (buildings, roads, props, carts, walls,
# crates, weapons) keeps the 30 deg smoothing fans so corners stay crisp.
SMOOTH_RE = re.compile(r'Tree|Bush|Plant|Flower|Grass|Reeds|Hedge|Lillypads|Mushroom|'
                       r'GroundMounds|DirtMound|Hill|SnowPile|Cloud|Rock|Ice|Stream|'
                       r'Waterfall|Seaweed|Coral|Cattails|Vine')


def key(co):
    return (round(co[0], 5), round(co[1], 5), round(co[2], 5))


def compute_normals(me, fan_angle_cos):
    """Per-loop normals. fan_angle_cos=None -> all faces around the vertex position (smooth);
    otherwise faces reachable from the loop's face through edges at this vertex whose dihedral
    cos >= fan_angle_cos (auto). Area-weighted."""
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
    """One smooth (all-faces, area-weighted) normal per vertex position, for the shell offset."""
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


def toonify_one(src, dst):
    base = os.path.splitext(os.path.basename(src))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, use_custom_normals=True)

    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        return {'ok': False, 'error': 'no meshes'}

    mode = 'smooth' if SMOOTH_RE.search(base) else 'auto'
    outline_mat = bpy.data.materials.new(OUTLINE_MAT)
    outline_mat.name = OUTLINE_MAT
    base_mat = None
    parts = []

    for obj in list(meshes):
        me = obj.data
        # --- material slot -> pack name ---------------------------------------------------------
        if base_mat is None:
            base_mat = obj.material_slots[0].material if obj.material_slots else bpy.data.materials.new(BASE_MAT)
            base_mat.name = BASE_MAT
            if base_mat.name != BASE_MAT:   # a stale datablock held the name; steal it
                bpy.data.materials.remove(bpy.data.materials[BASE_MAT])
                base_mat.name = BASE_MAT
        me.materials.clear()
        me.materials.append(base_mat)

        for v in me.vertices:
            v.co *= UNIT_MUL
        me.update()

        for p in me.polygons:
            p.use_smooth = True

        # --- outline shell BEFORE touching base normals (positions are all it needs) ------------
        vn = vertex_smooth_normals(me)
        shell = obj.copy()
        shell.data = me.copy()
        # Always from the NODE name: a few Synty files have node != filename, and the vmdl
        # exception_list is keyed by node names.
        shell.name = obj.name + "_Outline"
        shell.data.name = shell.name
        bpy.context.scene.collection.objects.link(shell)
        sme = shell.data
        if OFFSET:
            for v, n in zip(sme.vertices, vn):
                v.co.x += n[0] * OFFSET
                v.co.y += n[1] * OFFSET
                v.co.z += n[2] * OFFSET
        sme.materials.clear()
        sme.materials.append(outline_mat)
        shell_loops = [vn[sme.loops[l].vertex_index] for l in range(len(sme.loops))]
        sme.normals_split_custom_set(shell_loops)

        # --- base normals ----------------------------------------------------------------------
        me.normals_split_custom_set(compute_normals(me, None if mode == 'smooth' else COS30))

        parts.append({'name': obj.name, 'polys': len(me.polygons), 'outline': shell.name})

    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=False,
        mesh_smooth_type='FACE',
        use_custom_props=False,
        use_mesh_modifiers=False,
        add_leaf_bones=False,
        path_mode='AUTO',
    )
    return {'ok': True, 'mode': mode, 'slot': BASE_MAT, 'parts': parts}


os.makedirs(OUT, exist_ok=True)
RESULTS = os.path.join(OUT, "_results.json")
results = {}
if os.path.exists(RESULTS):
    try:
        results = json.load(open(RESULTS, encoding='utf-8'))
    except Exception:
        results = {}

files = sorted(f for f in os.listdir(SRC) if f.lower().endswith('.fbx'))[START:START + COUNT]
done = fail = copied = 0
for i, f in enumerate(files):
    src = os.path.join(SRC, f)
    dst = os.path.join(OUT, f)
    if results.get(f, {}).get('ok'):
        continue
    if f.startswith('FX_'):
        shutil.copyfile(src, dst)
        results[f] = {'ok': True, 'copied': True}
        copied += 1
        continue
    try:
        r = toonify_one(src, dst)
    except Exception as e:
        r = {'ok': False, 'error': repr(e)[:300]}
    results[f] = r
    if r.get('ok'):
        done += 1
    else:
        fail += 1
        print("TOON_FAIL %s :: %s" % (f, r.get('error')))
    if (i + 1) % 25 == 0 or i + 1 == len(files):
        json.dump(results, open(RESULTS, 'w', encoding='utf-8'), indent=0)
        print("TOON_PROGRESS %d/%d ok=%d fail=%d" % (i + 1, len(files), done, fail))
        sys.stdout.flush()

json.dump(results, open(RESULTS, 'w', encoding='utf-8'), indent=0)
print("TOON_DONE ok=%d copied=%d fail=%d total=%d" % (done, copied, fail, len(files)))
