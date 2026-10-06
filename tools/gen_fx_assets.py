"""Import a chosen subset of the Synty POLYGON Particle FX meshes as vmdls on the unlit fx materials.

    python tools/gen_fx_assets.py [--src "<folder with the FBX>"]

Copies each FBX to Assets/VFX/models/<lowercase>.fbx, reads its Model node names, material slot names and
vertex extent with fbx_bounds.py (binary FBX only; the one ASCII file, SM_LightRayRound 1, is skipped), picks
the import scale from the extent (cm-authored -> 0.3937, the metre-authored outliers -> 39.37, the same rule
gen_dungeon_assets.py applies by name) and writes a no-physics vmdl in the fx_ring_mesh.vmdl format with every
slot remapped to shaders/fx_unlit_<add|alpha>.vmat. The editor compiles the vmdls on open; `spell_fx <name>`
spawns one in front of the player for a size check.
"""
import os, shutil, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fbx_bounds

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DEFAULT_SRC = r"C:\Users\skipp\Desktop\Speelboord Studios\PolygonFx\FBX"
OUT = os.path.join(PROJ, "Assets", "VFX", "models")

# stem -> which fx material: "add" glows, "alpha" is a solid, "gore" is the toon-lit meat of BloodVfx's gibs
# (shaders/GoreChunk.vmat, heden_toon on a flat dark-red albedo)
MATERIALS = {"add": "shaders/fx_unlit_add.vmat", "alpha": "shaders/fx_unlit_alpha.vmat", "gore": "shaders/gorechunk.vmat"}
SELECT = {
    "SM_GoreChunk_01": "gore", "SM_GoreChunk_02": "gore", "SM_GoreChunk_03": "gore", "SM_GoreChunk_04": "gore",
    "FX_Crystal_01": "alpha", "FX_Crystal_02": "alpha", "FX_Crystal_03": "alpha", "FX_Crystal_04": "alpha",
    "FX_CrystalShard_01": "alpha",
    "FX_Shard_Rock_01": "alpha", "FX_Shard_Rock_02": "alpha", "FX_Shard_Rock_Small_01": "alpha",
    "FX_Sphere_01": "add", "FX_Sphere_02": "add", "FX_Sphere_03": "add", "FX_Sphere_Puff_01": "alpha",
    "FX_Sphere_Spikes_01": "add", "FX_Sphere_Spikes_02": "add",
    "FX_Cone_01": "add", "FX_Cone_02": "add", "FX_Cylinder_01": "add",
    "FX_Ring_01": "add", "FX_Star": "add", "FX_Spark_01": "add",
    "SM_LightRayCube 1": "add", "SM_Flame_FX": "add", "SM_Particle_Smoke_01": "alpha",
}

VMDL = '''<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->
{
	rootNode =
	{
		_class = "RootNode"
		children =
		[
			{
				_class = "MaterialGroupList"
				children =
				[
					{
						_class = "DefaultMaterialGroup"
						remaps =
						[
%(remaps)s						]
						use_global_default = false
						global_default_material = "materials/default.vmat"
					},
				]
			},
			{
				_class = "RenderMeshList"
				children =
				[
					{
						_class = "RenderMeshFile"
						filename = "vfx/models/%(fbx)s"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = %(scale)s
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{
							exclude_by_default = true
							exception_list =
							[
%(nodes)s							]
						}
					},
				]
			},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}
}
'''


def scan(path):
    """Model node names, material names and the largest vertex extent (source units)."""
    version, nodes = fbx_bounds.parse(path)
    objects = fbx_bounds.find(nodes, "Objects")
    models, materials, extent = [], [], 0.0
    for obj in objects:
        for child in obj[2]:
            name, props, kids = child
            if name == "Model":
                label = props[1].decode("latin1").split("\x00")[0] if len(props) > 1 else ""
                if label: models.append(label)
            elif name == "Material":
                label = props[1].decode("latin1").split("\x00")[0] if len(props) > 1 else ""
                if label: materials.append(label)
            elif name == "Geometry":
                for g in kids:
                    if g[0] == "Vertices":
                        v = g[1][0]
                        lo = [min(v[i::3]) for i in range(3)]
                        hi = [max(v[i::3]) for i in range(3)]
                        extent = max(extent, max(hi[i] - lo[i] for i in range(3)))
    return models, materials, extent


def main():
    src = DEFAULT_SRC
    if "--src" in sys.argv:
        src = sys.argv[sys.argv.index("--src") + 1]
    os.makedirs(OUT, exist_ok=True)
    rows = []
    for stem, kind in SELECT.items():
        fbx = os.path.join(src, stem + ".fbx")
        if not os.path.exists(fbx):
            print("missing", fbx); continue
        with open(fbx, "rb") as f:
            head = f.read(20)
        if not head.startswith(b"Kaydara FBX Binary"):
            print("skip (ASCII FBX):", stem); continue
        out_stem = stem.lower().replace(" ", "_")
        dst = os.path.join(OUT, out_stem + ".fbx")
        shutil.copyfile(fbx, dst)
        models, materials, extent = scan(dst)
        # cm-authored meshes are tens to hundreds of units across; the metre-authored outliers are under 5
        scale = 39.37 if extent < 5.0 else 0.3937
        remaps = "".join('							{\n								from = "%s.vmat"\n								to = "%s"\n							},\n' % (m, MATERIALS[kind]) for m in materials)
        node_list = "".join('								"%s",\n' % m for m in models)
        text = VMDL % {"remaps": remaps, "fbx": out_stem + ".fbx", "scale": scale, "nodes": node_list}
        with open(os.path.join(OUT, out_stem + ".vmdl"), "w", newline="\n") as f:
            f.write(text)
        rows.append((out_stem, scale, extent * scale, ", ".join(materials), ", ".join(models)))
    print("%-28s %-7s %-10s %-24s %s" % ("vmdl", "scale", "size (u)", "materials", "nodes"))
    for r in rows:
        print("%-28s %-7s %-10.1f %-24s %s" % r)


if __name__ == "__main__":
    main()
