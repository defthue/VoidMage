"""Extract one Dungeon Realms demo scene and every prefab out of the Synty unitypackage, in the layout
unity_scene_to_placements.py reads (Demo.unity, guid2path.json, prefabs/<guid>.prefab).

    python tools/extract_realms_scene.py <out_dir> [Demo_Hell_01]

The package is a gzip tar of guid folders (`pathname`, `asset`, `asset.meta`): one pass builds the guid
index, a second pulls the scene and the prefabs (extract_adventure.py's method, minus the FBX, which are
already toonified in Assets/DungeonRealms).
"""
import json
import os
import sys
import tarfile

LIB = r"C:\Users\skipp\Desktop\Speelboord Studios"
PKG = os.path.join(LIB, "POLYGON_Dungeon_Realms_Unity_2022_3_v1_4_5.unitypackage")
SCENE_DIR = "Assets/Synty/PolygonDungeonRealms/Scenes/"

OUT = os.path.abspath(sys.argv[1])
SCENE = sys.argv[2] if len(sys.argv) > 2 else "Demo_Hell_01"
os.makedirs(os.path.join(OUT, "prefabs"), exist_ok=True)

guid2path = {}
with tarfile.open(PKG, mode="r|gz") as tf:
    for m in tf:
        if m.isfile() and m.name.endswith("/pathname"):
            guid2path[m.name.split("/")[0]] = tf.extractfile(m).read().decode("utf-8", "replace").split("\n")[0].strip()
json.dump(guid2path, open(os.path.join(OUT, "guid2path.json"), "w"), indent=0)
print("guid2path:", len(guid2path), flush=True)

scene_guid = {p: g for g, p in guid2path.items()}[SCENE_DIR + SCENE + ".unity"]
want = {g for g, p in guid2path.items() if "/PolygonDungeonRealms/" in p and p.endswith(".prefab")}
want.add(scene_guid)

got = 0
with tarfile.open(PKG, mode="r|gz") as tf:
    for m in tf:
        if not m.isfile():
            continue
        parts = m.name.split("/")
        if len(parts) != 2 or parts[1] != "asset" or parts[0] not in want:
            continue
        data = tf.extractfile(m).read()
        dst = os.path.join(OUT, "Demo.unity") if parts[0] == scene_guid else os.path.join(OUT, "prefabs", parts[0] + ".prefab")
        open(dst, "wb").write(data)
        got += 1
print("assets:", got, "->", OUT, flush=True)
