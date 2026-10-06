"""
Render the inventory HUD's scroll icons (2026-09-17) from the Dungeon Realms scroll models.

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/render_scroll_icons.py

Writes Assets/ui/scrolls/{closed,open}_{mod,spell}.png (512 px, transparent). Enchantment scrolls wear atlas 01_a
(red wax seal, the pack's default), spell scrolls atlas 04_a (gold seal): the same two atlases the world pickup uses
(shaders/realmsshader.vmat and shaders/realmsscrollspell.vmat). The open scroll is shot straight down so its paper is a
flat rectangle the HUD writes the rune code on. Flat texture colour plus a dark silhouette outline, close to the
toon look; placeholder until the UI sweep.
"""
import bpy, os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "Assets")
TEX = os.path.join(ASSETS, "Textures", "DungeonRealmsTextures")
OUT = os.path.join(ASSETS, "ui", "scrolls")
ATLAS = {"mod": "01_a", "spell": "04_a"}
MODELS = {"closed": "SM_Prop_Scroll_01.fbx", "open": "SM_Prop_Scroll_Open_01.fbx"}


def render(shape, kind):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ASSETS, "DungeonRealms", MODELS[shape]))
    img = bpy.data.images.load(os.path.join(TEX, f"dungeons_2_texture_{ATLAS[kind]}.png"))
    meshes = []
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            continue
        if "outline" in o.name.lower():
            # the pack's outline hull is an exact copy the game's shader inflates; here it would only cover the model
            o.hide_render = True
            continue
        mat = bpy.data.materials.new("scroll")
        mat.use_nodes = True
        nt = mat.node_tree
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = 'Closest'
        nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
        o.data.materials.clear()
        o.data.materials.append(mat)
        meshes.append(o)

    mn = Vector((1e9,) * 3)
    mx = Vector((-1e9,) * 3)
    for o in meshes:
        for v in o.bound_box:
            w = o.matrix_world @ Vector(v)
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    centre = (mn + mx) / 2
    size = max(mx - mn)

    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    direction = Vector((0.0, -0.001, 1.0)) if shape == "open" else Vector((0.9, -1.0, 0.8))
    cam.location = centre + direction.normalized() * size * 3
    cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
    # the open scroll fills the frame edge to edge; the closed bundle sits with a little air around it
    cam.data.ortho_scale = size * (1.1 if shape == "open" else 1.1)

    sc = bpy.context.scene
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'FLAT'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_object_outline = False
    sc.view_settings.view_transform = 'Standard'
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.filepath = os.path.join(OUT, f"{shape}_{kind}.png")
    bpy.ops.render.render(write_still=True)
    ink_outline(sc.render.filepath, INK_PX)
    print(f"[scroll icons] {sc.render.filepath}")


INK_PX = 7


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
            if dx * dx + dy * dy > radius * radius:
                continue
            grown |= np.roll(np.roll(alpha, dy, axis=0), dx, axis=1)
    ink = grown & ~alpha
    px[ink] = (0.02, 0.018, 0.022, 1.0)
    img.pixels[:] = px.ravel()
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()


os.makedirs(OUT, exist_ok=True)
for shape in MODELS:
    for kind in ATLAS:
        render(shape, kind)
