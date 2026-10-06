"""Extract everything the Adventure import needs out of the two Synty archives.

  python tools/extract_adventure.py <out_dir>

Neither archive is in the repo; both live in the asset library on the Desktop. A .unitypackage
is a gzip tar of guid-named folders, each holding `pathname` (the asset's project path), `asset`
(the payload) and `asset.meta` -- so resolving anything takes one pass to build the guid index
and a second to pull the payloads.

Writes into <out_dir>:
  fbx/*.fbx                268 Maya source meshes (the toonify input)
  PolyAdventureTexture_01.png
  Demo.unity               the demo scene YAML
  guid2path.json           guid -> asset path index
  prefabs/<guid>.prefab    every PolygonAdventure prefab (for prefab -> mesh resolution)
  meta/<guid>.meta         .meta for every PolygonAdventure fbx/prefab
"""
import os, sys, json, zipfile, tarfile

LIB = r"C:\Users\skipp\Desktop\Speelboord Studios"
SRC_ZIP = os.path.join(LIB, "POLYGON_Adventure_Pack_SourceFiles_v5.zip")
PKG = os.path.join(LIB, "POLYGON_Adventure_Unity_2022_3_v1_8_2.unitypackage")
SCENE = "Assets/Synty/PolygonAdventure/Scenes/Demo.unity"

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "adventure")

for sub in ("fbx", "prefabs", "meta"):
    os.makedirs(os.path.join(OUT, sub), exist_ok=True)

# ---------------------------------------------------------------- source FBX
# The SourceFiles zip, not the Unity package: these are the Maya exports toonify_adventure.py is
# calibrated for. The two sets differ by 5-6 filenames (a Sheild/Shield typo, some test meshes),
# none of which the demo scene uses.
z = zipfile.ZipFile(SRC_ZIP)
n = 0
for name in z.namelist():
    if name.startswith("SourceFiles/FBX/") and name.lower().endswith(".fbx"):
        with open(os.path.join(OUT, "fbx", name.split("/")[-1]), "wb") as fh:
            fh.write(z.read(name))
        n += 1
with open(os.path.join(OUT, "PolyAdventureTexture_01.png"), "wb") as fh:
    fh.write(z.read("SourceFiles/Textures/PolyAdventureTexture_01.png"))
print("FBX extracted:", n, flush=True)

# ---------------------------------------------------------------- unitypackage, pass 1
guid2path = {}
with tarfile.open(PKG, mode="r|gz") as tf:
    for m in tf:
        if m.isfile() and m.name.endswith("/pathname"):
            guid2path[m.name.split("/")[0]] = tf.extractfile(m).read().decode("utf-8", "replace").split("\n")[0].strip()
json.dump(guid2path, open(os.path.join(OUT, "guid2path.json"), "w"), indent=0)
print("guid2path:", len(guid2path), flush=True)

scene_guid = {p: g for g, p in guid2path.items()}[SCENE]
want_asset = {scene_guid}
want_meta = set()
for g, p in guid2path.items():
    if "/PolygonAdventure/" not in p:
        continue
    if p.endswith(".prefab"):
        want_asset.add(g)
        want_meta.add(g)
    elif p.lower().endswith(".fbx"):
        want_meta.add(g)

# ---------------------------------------------------------------- pass 2
got_a = got_m = 0
with tarfile.open(PKG, mode="r|gz") as tf:
    for m in tf:
        if not m.isfile():
            continue
        parts = m.name.split("/")
        if len(parts) != 2:
            continue
        guid, kind = parts
        if kind == "asset" and guid in want_asset:
            data = tf.extractfile(m).read()
            dst = os.path.join(OUT, "Demo.unity") if guid == scene_guid \
                else os.path.join(OUT, "prefabs", guid + ".prefab")
            open(dst, "wb").write(data)
            got_a += 1
        elif kind == "asset.meta" and guid in want_meta:
            open(os.path.join(OUT, "meta", guid + ".meta"), "wb").write(tf.extractfile(m).read())
            got_m += 1
print("assets:", got_a, "metas:", got_m, flush=True)
print("DONE ->", OUT)
