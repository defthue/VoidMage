"""The s&box side of the PSX Horror-Fantasy monsters (tools/psx_kinds.py): the texture out of each GLB, one toon vmat
per monster + the pack's outline vmat, and one vmdl per monster from the manifest tools/toonify_psx.py wrote.

    python tools/gen_psx_assets.py [--kind werewolf] [--force-vmats] [--force-textures]

Textures come from the GLB's embedded image (the one the model was exported with; the loose PNGs differ for five
monsters), written as `Assets/Textures/PsxHorrorTextures/<name>.png`. The vmat is heden_toon over it on the INDOOR
preset (gen_pack_assets.TOON, the dungeon look), never overwritten without --force-vmats (hand tuning survives).
The vmdl is gen_synty_chars.write_vmdl (one `body` render mesh = mesh + `_Outline`, do_not_discard on every bone,
ScaleAndMirror) with the scale derived here: psx_kinds `size` / the LARGEST extent of the exported FBX measured with
fbx_bounds.mesh_world_sizes (node scale applied -- the Quaternius lesson: never Blender's numbers, never the raw
vertices). Retuning a size = edit psx_kinds.py, rerun this.

A kind with `clips=True` (the elkdemon) also lists every FBX in Assets/Animations/PsxHorror/<Mesh>/ as an AnimFile:
looping by psx_kinds.LOOPING, an ExtractMotion child when the clip's root bone travels (gen_skeleton_npc_vmdl's
node text and threshold). The clips come from tools/retarget_synty_to_rig.py.
"""
import argparse, glob, json, os, re, struct, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import fbx_bounds  # noqa: E402
import psx_kinds  # noqa: E402
import gen_synty_chars  # noqa: E402
import gen_skeleton_npc_vmdl as npc_vmdl  # noqa: E402
from gen_pack_assets import TOON, OUTLINE_INDOOR, INDOOR, WHITE, write_vmat  # noqa: E402

SHADER_DIR = os.path.join(ROOT, "Assets", "Shaders")
LOOPING = re.compile(psx_kinds.LOOPING)


def anim_files(k, root_bone):
    """AnimFile nodes for every clip in the kind's animation folder (gen_skeleton_npc_vmdl's node text: looping by
    name, an ExtractMotion child when the root bone travels more than its threshold)."""
    folder = os.path.join(psx_kinds.ANIM_DIR, k["mesh"])
    clips = sorted(glob.glob(os.path.join(folder, "*.fbx")))
    if not clips:
        raise SystemExit("%s: clips=True but no FBX in %s (run tools/retarget_synty_to_rig.py)" % (k["name"], folder))
    text, n_loop, n_root = "", 0, 0
    for c in clips:
        name = os.path.splitext(os.path.basename(c))[0]
        _, moving = npc_vmdl.root_travel(c, root_bone, npc_vmdl.ROOT_TRAVEL_THRESHOLD_CM)
        looping = bool(LOOPING.search(name))
        n_loop += looping; n_root += moving
        text += npc_vmdl.anim(name, psx_kinds.ANIM_REL + k["mesh"] + "/" + os.path.basename(c), looping, moving)
    return text, len(clips), n_loop, n_root


def glb_texture(path):
    """The first embedded image of a GLB, as raw PNG bytes."""
    data = open(path, "rb").read()
    pos, js, blob = 12, None, None
    while pos < len(data):
        clen, ctype = struct.unpack_from("<II", data, pos); pos += 8
        chunk = data[pos:pos + clen]; pos += clen
        if ctype == 0x4E4F534A:
            js = json.loads(chunk)
        elif ctype == 0x004E4942:
            blob = chunk
    im = js["images"][0]
    if im.get("mimeType") != "image/png":
        raise SystemExit("%s: embedded image is %s, not PNG" % (path, im.get("mimeType")))
    bv = js["bufferViews"][im["bufferView"]]
    off = bv.get("byteOffset", 0)
    return blob[off:off + bv["byteLength"]]


def main():
    ap = argparse.ArgumentParser()
    psx_kinds.add_kind_arg(ap)
    ap.add_argument("--force-vmats", action="store_true")
    ap.add_argument("--force-textures", action="store_true")
    args = ap.parse_args()
    with open(psx_kinds.MANIFEST, encoding="utf-8") as f:
        manifest = json.load(f)
    os.makedirs(psx_kinds.TEX_DIR, exist_ok=True)
    cfg = gen_synty_chars.PACKS["psxhorror"]
    wrote_v = kept_v = 0
    if write_vmat(os.path.join(SHADER_DIR, psx_kinds.OUTLINE_MATERIAL + ".vmat"), OUTLINE_INDOOR, args.force_vmats):
        wrote_v += 1
    else:
        kept_v += 1
    for name in psx_kinds.selected(args.kind):
        k = psx_kinds.KINDS[name]
        entry = manifest.get(name)
        if not entry or not entry.get("ok"):
            print("  SKIP %s: %s" % (name, (entry or {}).get("error", "not in the manifest; run toonify_psx.py")))
            continue
        tex = os.path.join(psx_kinds.TEX_DIR, name + ".png")
        if args.force_textures or not os.path.exists(tex):
            with open(tex, "wb") as f:
                f.write(glb_texture(os.path.join(k["folder"], k["glb"])))
        body = TOON.format(albedo=psx_kinds.TEX_REL + name + ".png", tint=WHITE, **INDOOR)
        if write_vmat(os.path.join(SHADER_DIR, k["material"] + ".vmat"), body, args.force_vmats):
            wrote_v += 1
        else:
            kept_v += 1
        fbx = os.path.join(psx_kinds.OUT_DIR, entry["out"])
        sizes = fbx_bounds.mesh_world_sizes(fbx)
        extent = max(sizes[k["mesh"]])
        entry["scale"] = k["size"] / extent
        clips = ""
        if k["clips"]:
            entry["anims"], n, n_loop, n_root = anim_files(k, entry["root_bone"])
            clips = ", %d clips (%d looping, %d with root motion)" % (n, n_loop, n_root)
        else:
            entry.pop("anims", None)
        print("%-22s extent %6.1f cm (%s) -> size %3.0f u, scale %.4f, texture %s%s" % (
            name, extent, " x ".join("%.0f" % v for v in sizes[k["mesh"]]), k["size"], entry["scale"], os.path.basename(tex), clips))
        gen_synty_chars.write_vmdl(cfg, entry)
    print("vmats: %d written, %d left alone (--force-vmats to overwrite)" % (wrote_v, kept_v))


if __name__ == "__main__":
    main()
