"""
Render the Armory tab's catalogue icons (2026-09-18): one per weapon skin, from the very FBX the player holds.

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/render_weapon_icons.py [-- id ...]

Writes Assets/ui/weapons/<skin id>.png and <skin id>_shade.png (512 px, transparent; the shade is the unfound silhouette) and tools/out/weapon_icons_sheet.png to look them over.

Every skin in Code/Inventory/WeaponSkins.cs is a vmdl in Assets/PlayerWeapons (plus the two starters in DungeonPack).
Colours come from where the game gets them: the vmdl's material remap -> the vmat's AlbedoTexture x g_vTextureTint,
so the brass is brass and the blade is the blade. Lit by three flat toon bands off the normal, inked like the scroll
icons. The weapon lies on the icon's diagonal, tip to the top right; the camera looks at whichever side is broader
(some blades were modelled with the crossguard across X, most across Y).
"""
import bpy, os, re, sys, math, glob
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "Assets"))
OUT = os.path.join(ASSETS, "ui", "weapons")
SIZE = 512
INK_PX = 7
STARTERS = {"sword_ornate": "DungeonPack/sm_wep_ornate_sword_02.vmdl", "staff_gem": "DungeonPack/sm_wep_staff_gem_01.vmdl"}
# The hotbar's items (2026-09-19): `-- items` renders these instead, standing upright, to Assets/ui/items/<id>.png.
# The two flasks the player actually drinks: DungeonPack potion 07 (healing) and 06 (mana), corked.
ITEMS = {"potion_heal": "DungeonPack/sm_item_potion_07.vmdl", "potion_mana": "DungeonPack/sm_item_potion_06.vmdl"}
ITEM_OUT = os.path.join(ASSETS, "ui", "items")
# modelled head down (WeaponSkin.Flip)
FLIPPED = {"staff_darkfantasy"}
LIGHT = Vector((-0.45, -0.55, 0.7)).normalized()


def skins():
    found = dict(STARTERS)
    for path in sorted(glob.glob(os.path.join(ASSETS, "PlayerWeapons", "*.vmdl"))):
        found[os.path.basename(path)[:-5]] = os.path.relpath(path, ASSETS)
    return found


def read_vmdl(vmdl):
    text = open(os.path.join(ASSETS, vmdl), encoding="utf-8", errors="replace").read()
    remap = {a.lower(): b for a, b in re.findall(r'from\s*=\s*"([^"]+)"\s*to\s*=\s*"([^"]+)"', text)}
    fbx = re.search(r'filename\s*=\s*"([^"]+\.fbx)"', text, re.I).group(1)
    return fbx, remap


def find_asset(rel):
    """Asset paths are lower-case in the vmdl; the folders on disk are not."""
    cur = ASSETS
    for part in rel.replace("\\", "/").split("/"):
        hit = next((n for n in os.listdir(cur) if n.lower() == part.lower()), None)
        if hit is None:
            return None
        cur = os.path.join(cur, hit)
    return cur


def read_vmat(rel):
    path = find_asset(rel)
    if not path:
        return None, (1, 1, 1)
    text = open(path, encoding="utf-8", errors="replace").read()
    tex = re.search(r'AlbedoTexture\s+"([^"]+)"', text)
    tint = re.search(r'g_vTextureTint\s+"\[([\d. ]+)\]"', text)
    rgb = tuple(float(v) for v in tint.group(1).split()[:3]) if tint else (1, 1, 1)
    return (find_asset(tex.group(1)) if tex else None), rgb


def toon_material(name, tex_path, tint):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    dot = nt.nodes.new("ShaderNodeVectorMath"); dot.operation = 'DOT_PRODUCT'
    dot.inputs[1].default_value = LIGHT
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = 'CONSTANT'
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = 0.0, (0.5, 0.5, 0.56, 1)
    els[1].position, els[1].color = 0.42, (0.78, 0.78, 0.8, 1)
    top = els.new(0.72); top.color = (1, 1, 1, 1)
    remap = nt.nodes.new("ShaderNodeMath"); remap.operation = 'MULTIPLY_ADD'
    remap.inputs[1].default_value = 0.5; remap.inputs[2].default_value = 0.5
    mul = nt.nodes.new("ShaderNodeMixRGB"); mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
    tinted = nt.nodes.new("ShaderNodeMixRGB"); tinted.blend_type = 'MULTIPLY'; tinted.inputs[0].default_value = 1.0
    tinted.inputs[2].default_value = (*tint, 1)
    if tex_path:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(tex_path, check_existing=True)
        tex.interpolation = 'Closest'
        nt.links.new(tex.outputs["Color"], tinted.inputs[1])
    else:
        tinted.inputs[1].default_value = (0.6, 0.6, 0.62, 1)
    nt.links.new(geo.outputs["Normal"], dot.inputs[0])
    nt.links.new(dot.outputs["Value"], remap.inputs[0])
    nt.links.new(remap.outputs[0], ramp.inputs[0])
    nt.links.new(tinted.outputs[0], mul.inputs[1])
    nt.links.new(ramp.outputs["Color"], mul.inputs[2])
    nt.links.new(mul.outputs[0], emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    return mat


def render(skin, vmdl, upright=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    fbx, remap = read_vmdl(vmdl)
    bpy.ops.import_scene.fbx(filepath=find_asset(fbx))
    meshes = []
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            continue
        if "outline" in o.name.lower():
            o.hide_render = True
            continue
        # the outline shell can also be a slot on the same mesh: drop its faces
        hull = [i for i, s in enumerate(o.material_slots) if s.material and "outline" in s.material.name.lower()]
        if hull:
            import bmesh
            bm = bmesh.new(); bm.from_mesh(o.data)
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in hull], context='FACES')
            bm.to_mesh(o.data); bm.free()
        for slot in o.material_slots:
            if not slot.material:
                continue
            key = re.sub(r"\.\d+$", "", slot.material.name).lower() + ".vmat"
            tex, tint = read_vmat(remap.get(key, "shaders/" + key))
            slot.material = toon_material(slot.material.name + "_icon", tex, tint)
        meshes.append(o)

    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector([min(p[i] for p in pts) for i in range(3)])
    mx = Vector([max(p[i] for p in pts) for i in range(3)])
    centre, ext = (mn + mx) / 2, mx - mn

    # look at the broad side, a little off square so the low-poly facets catch different bands
    yaw = math.radians(18)
    side = Vector((0, -1, 0)) if ext.x >= ext.y else Vector((-1, 0, 0))
    view = (Matrix.Rotation(yaw, 3, 'Z') @ side + Vector((0, 0, 0.12))).normalized()
    up = Vector((0, 0, -1 if skin in FLIPPED else 1))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.location = centre + view * max(ext) * 4
    quat = (-view).to_track_quat('-Z', 'Y')
    basis = quat.to_matrix()
    if basis.col[1].dot(up) < 0:            # head down: stand it on its head
        basis = basis @ Matrix.Rotation(math.pi, 3, 'Z')
    if not upright:
        basis = basis @ Matrix.Rotation(math.radians(45), 3, 'Z')      # the diagonal, tip to the top right
    cam.rotation_euler = basis.to_euler()
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    span = max(max(abs((inv @ p).x), abs((inv @ p).y)) for p in pts)
    cam.data.ortho_scale = span * 2 * 1.12
    cam.data.clip_end = max(ext) * 20

    sc = bpy.context.scene
    sc.camera = cam
    try:
        sc.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.view_settings.view_transform = 'Standard'
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = SIZE
    sc.render.resolution_percentage = 100
    sc.render.filepath = os.path.join(ITEM_OUT if upright else OUT, skin + ".png")
    bpy.ops.render.render(write_still=True)
    ink_outline(sc.render.filepath, INK_PX)
    print(f"[weapon icons] {skin}: ext {tuple(round(v, 1) for v in ext)}", flush=True)


def ink_outline(path, radius):
    """The toon ink line: the silhouette grown by `radius` px, drawn dark behind the colour."""
    import numpy as np
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    alpha = px[:, :, 3] > 0.5
    grown = alpha.copy()
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            if dx * dx + dy * dy <= radius * radius:
                grown |= np.roll(np.roll(alpha, dy, axis=0), dx, axis=1)
    px[grown & ~alpha] = (0.02, 0.018, 0.022, 1.0)
    px[alpha, 3] = 1.0
    img.pixels[:] = px.ravel()
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()

    # <id>_shade.png: the same shape with nothing in it, for a weapon not found yet
    shade = np.zeros_like(px)
    shade[grown] = (0.34, 0.34, 0.42, 1.0)
    shade[alpha] = (0.11, 0.11, 0.15, 1.0)
    out = bpy.data.images.new("shade", w, h, alpha=True)
    out.pixels[:] = shade.ravel()
    out.filepath_raw = path[:-4] + "_shade.png"
    out.file_format = 'PNG'
    out.save()


def sheet(ids):
    import numpy as np
    cols = 8
    rows = (len(ids) + cols - 1) // cols
    cell = 256
    canvas = np.zeros((rows * cell, cols * cell, 4), dtype=np.float32)
    canvas[:, :, :3] = (0.07, 0.07, 0.1); canvas[:, :, 3] = 1
    for k, skin in enumerate(ids):
        img = bpy.data.images.load(os.path.join(OUT, skin + ".png"))
        px = np.array(img.pixels[:], dtype=np.float32).reshape(SIZE, SIZE, 4)[::2, ::2]
        r, c = rows - 1 - k // cols, k % cols
        dst = canvas[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell]
        a = px[:, :, 3:4]
        dst[:, :, :3] = px[:, :, :3] * a + dst[:, :, :3] * (1 - a)
    out = bpy.data.images.new("sheet", cols * cell, rows * cell, alpha=True)
    out.pixels[:] = canvas.ravel()
    out.filepath_raw = os.path.join(HERE, "out", "weapon_icons_sheet.png")
    out.file_format = 'PNG'
    out.save()


os.makedirs(OUT, exist_ok=True)
os.makedirs(ITEM_OUT, exist_ok=True)
os.makedirs(os.path.join(HERE, "out"), exist_ok=True)
wanted = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if "items" in wanted:
    for item, item_vmdl in ITEMS.items():
        render(item, item_vmdl, upright=True)
    sys.exit(0)
all_skins = skins()
for skin, vmdl in all_skins.items():
    if not wanted or skin in wanted:
        render(skin, vmdl)
sheet(sorted(all_skins))
