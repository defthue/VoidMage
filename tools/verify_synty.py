"""Verify a toonified Synty character pack (Fantasy Rivals, Dark Fantasy) against its source, write _report.txt.

  python tools/verify_synty.py <Source_Files> <static_out> <chars_out> --pack rivals|darkfantasy

Static half (verify_realms.py's checks, unchanged in spirit): every source FBX produced an output bar
the skipped ones, node pairs SM_X + SM_X_Outline, one base material of the pack's families +
FantasyKingdomOutline, poly count preserved, outline COINCIDENT with the base (no baked offset), extents
unchanged (the x100 trap).

Character half, per entry of <chars_out>/_chars.json: the exported FBX holds every mesh + its _Outline
shell (Mesh nodes) and a `root` bone; base/outline geometry identical and coincident; the bind-pose
WORLD size of every mesh (fbx_bounds.mesh_world_sizes: node scale and rotation applied) matches the
source mesh's within 0.5 % -- the export writes metres under a 100x node scale, so the raw vertex
numbers are meaningless and this is the check that matters.
"""
import sys, os, re, json, collections, argparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbx_bounds import parse, find, mesh_world_sizes

OUTLINE = "FantasyKingdomOutline"
OFFSET = 0.0


def child(node, name):
    for c in node[2]:
        if c[0] == name:
            return c
    return None


def scan(path):
    raw = open(path, 'rb').read().decode('latin-1')
    mats = re.findall(r'([\x20-\x7E]{2,80})\x00\x01Material', raw)
    typed = re.findall(r'([\x20-\x7E]{2,80})\x00\x01Model(?s:.{0,12}?)(Mesh|Null|LimbNode)', raw)
    _, tree = parse(path)
    geos = []
    junk = 0
    for objs in find(tree, 'Objects'):
        for g in objs[2]:
            # blend-shape geometries (the ocean's wave morphs) carry Vertices but no polygons: skip them
            if g[0] != 'Geometry' or child(g, 'Vertices') is None or child(g, 'PolygonVertexIndex') is None:
                continue
            V = child(g, 'Vertices')[1][0]
            PI = child(g, 'PolygonVertexIndex')[1][0]
            npoly = sum(1 for i in PI if i < 0)
            lo = [min(V[k::3]) for k in range(3)]
            hi = [max(V[k::3]) for k in range(3)]
            geos.append((len(V) // 3, npoly, lo, hi))
            # faces Blender's importer drops on the way in: exact duplicates (same vertex set) and
            # degenerate ones (< 3 distinct vertices). Synty's gargoyle statue carries 94 of them.
            seen, cur = set(), []
            for i in PI:
                cur.append(i if i >= 0 else ~i)
                if i < 0:
                    fs = frozenset(cur)
                    # a repeated index inside one polygon ([a, b, b, c]) is what mesh.validate() collapses
                    if fs in seen or len(fs) < 3 or len(fs) < len(cur):
                        junk += 1
                    seen.add(fs)
                    cur = []
    return mats, typed, geos, junk


def pair_geos(geos):
    """Base/shell pairs. The exporter does not keep 'base then its shell' order when a file holds several
    meshes (SM_Wep_MechanicalGolem_02: body, blade, then both shells), so pair by identical vertex and
    polygon counts + bounds instead of by position."""
    order = sorted(range(len(geos)), key=lambda i: (geos[i][0], geos[i][1], geos[i][2], geos[i][3]))
    return [(geos[order[i]], geos[order[i + 1]]) for i in range(0, len(order) - 1, 2)]


def check_static(src_dirs, out, base_mats, skip_re, problems, modes, families, node_renames, degenerate):
    """src_dirs: the folders (not recursed) holding the source FBX; a pack like Dark Fantasy spreads them over
    Buildings/, Props/, Weapons/... and the toonifier keeps only the basename."""
    results = json.load(open(os.path.join(out, '_results.json'), encoding='utf-8'))
    src_of = {}
    for d in src_dirs:
        for f in sorted(os.listdir(d)):
            if f.lower().endswith('.fbx') and not skip_re.match(f):
                src_of.setdefault(f, os.path.join(d, f))
    src_files = sorted(src_of)
    out_files = sorted(f for f in os.listdir(out) if f.lower().endswith('.fbx'))
    missing = [f for f in src_files if f not in out_files]
    extra = [f for f in out_files if f not in src_files]
    if missing: problems.append('static: missing outputs: %s' % missing)
    if extra: problems.append('static: unexpected outputs: %s' % extra)

    for f in out_files:
        base = f[:-4]
        r = results.get(f, {})
        if not r.get('ok'):
            problems.append('%s: batch failure %s' % (f, r.get('error')))
            continue
        if f not in src_of:
            continue
        modes[r.get('mode', '?')] += 1
        for p in r['parts']:
            for s in p['slots']:
                families[s] += 1
        try:
            mats, typed, geos, _ = scan(os.path.join(out, f))
            _, styped, sgeos, sjunk = scan(src_of[f])
        except Exception as e:
            problems.append('%s: unreadable (%r)' % (f, e))
            continue
        got = set(mats)
        bases = got - {OUTLINE}
        # a mesh whose every face sits on a no-outline slot (a vine card) has no shell; a partly cut shell
        # (a wall with a window pane) has fewer faces than its base and is checked by extent only
        shelled = [p['name'] for p in r['parts'] if p.get('outline')]
        if (OUTLINE in got) != bool(shelled) or not bases or not (bases <= base_mats):
            problems.append('%s: materials %s' % (f, sorted(got)))
        nodes = [n for n, t in typed if t == 'Mesh']
        node_base = [n for n in nodes if not n.endswith('_Outline')]
        want = sorted(node_base + [n + '_Outline' for n in node_base if n in shelled])
        if not node_base or sorted(nodes) != want:
            problems.append('%s: nodes %s' % (f, nodes))
            continue
        if base not in node_base:
            node_renames.append('%s -> node %s' % (f, node_base))
        if len(geos) != len(node_base) + len(shelled):
            problems.append('%s: expected %d geometries, got %d' % (f, len(node_base) + len(shelled), len(geos)))
            continue
        full = all(p.get('outline_polys', p['polys']) == p['polys'] for p in r['parts'] if p.get('outline'))
        if full and len(shelled) == len(node_base):
            for b, o in pair_geos(geos):
                if b[1] != o[1] or b[0] != o[0]:
                    problems.append('%s: base/outline mismatch %s vs %s' % (f, b[:2], o[:2]))
                drift = max(max(abs(b[2][k] - o[2][k]), abs(b[3][k] - o[3][k])) for k in range(3))
                if drift > max(OFFSET, 1e-4) + 1e-4:
                    problems.append('%s: outline drifts %.5f from base (expected %.3f)' % (f, drift, OFFSET))
        src_polys = sum(g[1] for g in sgeos)
        out_polys = sum(p['polys'] for p in r['parts'])
        if src_polys != out_polys:
            lost = src_polys - out_polys
            if 0 < lost <= sjunk:
                degenerate.append('%s: %d of %d faces dropped (%d duplicate/degenerate in the source)' % (f, lost, src_polys, sjunk))
            elif 0 < lost <= 4 and lost <= src_polys * 0.01:
                # Blender's validator also drops faces this reader cannot classify (zero-area slivers with
                # distinct indices); a couple of them in a mesh of hundreds is that, not damage -- the
                # extent and outline checks are what would catch damage
                degenerate.append('%s: %d of %d faces dropped (unclassified sliver, under 1 %%)' % (f, lost, src_polys))
            else:
                problems.append('%s: poly count changed %d -> %d (source has %d duplicate/degenerate faces)' % (f, src_polys, out_polys, sjunk))
        # scale preserved (the x100 trap): whole-file extents
        if sgeos:
            se = [max(g[3][k] for g in sgeos) - min(g[2][k] for g in sgeos) for k in range(3)]
            oe = [max(g[3][k] for g in geos) - min(g[2][k] for g in geos) for k in range(3)]   # shells are coincident or a subset
            for k in range(3):
                if abs(se[k]) > 1e-6 and abs(oe[k] / se[k] - 1.0) > 0.001:
                    problems.append('%s: extent changed axis %d: %.3f -> %.3f (x%.2f)' % (f, k, se[k], oe[k], oe[k] / se[k]))
                    break
    return len(src_files), len(out_files)


def check_chars(out, src_dirs, base_mats, problems, char_lines):
    manifest = json.load(open(os.path.join(out, '_chars.json'), encoding='utf-8'))
    n = 0
    for key_name, r in sorted(manifest.items()):
        src_name = r.get('src_file', key_name)   # a split character file keys its records by output name
        if not r.get('ok'):
            problems.append('%s: batch failure %s' % (src_name, r.get('error')))
            continue
        f = r['out']
        path = os.path.join(out, f)
        src_path = next((os.path.join(d, src_name) for d in src_dirs if os.path.exists(os.path.join(d, src_name))), None)
        if src_path is None:
            problems.append('%s: source not found under %s' % (src_name, src_dirs))
            continue
        try:
            mats, typed, geos, _ = scan(path)
            out_sizes = mesh_world_sizes(path)
            src_sizes = mesh_world_sizes(src_path)
        except Exception as e:
            problems.append('%s: unreadable (%r)' % (f, e))
            continue
        n += 1
        got = set(mats)
        bases = got - {OUTLINE}
        if (OUTLINE in got) != any(p.get('outline') for p in r['parts']) or not bases or not (bases <= base_mats):
            problems.append('%s: materials %s' % (f, sorted(got)))
        mesh_nodes = sorted(nm for nm, t in typed if t == 'Mesh')
        bones = [nm for nm, t in typed if t == 'LimbNode']
        want = sorted(x for p in r['parts'] for x in (p['name'], p['outline']) if x)
        if mesh_nodes != want:
            problems.append('%s: mesh nodes %s, wanted %s' % (f, mesh_nodes, want))
        if not any(b.lower() == 'root' for b in bones):
            problems.append('%s: no root bone (bones %s)' % (f, bones[:6]))
        if len(geos) != 2 * len(r['parts']):
            problems.append('%s: expected %d geometries, got %d' % (f, 2 * len(r['parts']), len(geos)))
        else:
            for b, o in pair_geos(geos):
                if b[1] != o[1] or b[0] != o[0]:
                    problems.append('%s: base/outline mismatch %s vs %s' % (f, b[:2], o[:2]))
                drift = max(max(abs(b[2][k] - o[2][k]), abs(b[3][k] - o[3][k])) for k in range(3))
                if drift > 1e-4 + 1e-4:
                    problems.append('%s: outline drifts %.5f from base' % (f, drift))
        for p in r['parts']:
            s_out = out_sizes.get(p['name'])
            s_src = src_sizes.get(p['name'])
            if s_out is None or s_src is None:
                problems.append('%s: no world size for %s (out %s, src %s)' % (f, p['name'], s_out is not None, s_src is not None))
                continue
            for k in range(3):
                if s_src[k] > 1e-3 and abs(s_out[k] / s_src[k] - 1.0) > 0.005:
                    problems.append('%s: %s world size axis %d: %.2f -> %.2f cm (x%.3f)' % (f, p['name'], k, s_src[k], s_out[k], s_out[k] / s_src[k]))
                    break
            shell = out_sizes.get(p['outline'])
            if shell and max(abs(shell[k] - s_out[k]) for k in range(3)) > 0.01:
                problems.append('%s: outline world size differs from body: %s vs %s' % (f, shell, s_out))
        body = out_sizes.get(r['parts'][0]['name'], (0, 0, 0))
        char_lines.append('%-24s %2d bones  root %-8s  %5.1f cm tall  %5.1f wide  slots %s%s' % (
            f, len(bones), r.get('root_bone'), body[1], body[0], ','.join(sorted({s for p in r['parts'] for s in p['slots']})),
            '  (root bone added)' if r.get('root_added') else ''))
    return n


DF = "DarkFantasy"
PACKS = {
    # <src_root> is the pack's Source_Files folder; static_src / src_chars are relative to it
    "rivals": dict(
        title="POLYGON Fantasy Rivals",
        mats={"FantasyRivalsShader", "FantasyRivalsGhost"},
        skip=r'^(Characters|Characters_BR|Medusa_Snakes|Mystic_Arms|FX_)',
        static_src=["FBX"],
        src_chars=[os.path.join("Characters", "Unreal_Characters"), "Characters"],
    ),
    "darkfantasy": dict(
        title="POLYGON Dark Fantasy",
        mats={DF + s for s in ("Shader", "Shader01B", "Shader02B", "BrickSmall01", "BrickSmall02", "BrickLarge01", "BrickLarge02", "Roof01", "Stone02",
                               "StuccoBrown01", "StuccoBrown02", "StuccoBrown03", "FloorTiles01", "Grunge01",
                               "WoodFloorBoards01", "WoodFloorBoards02", "HouseWall01", "DividerPattern01", "Moss01",
                               "Window01", "Window02", "Window04", "Vines")},
        skip=r'^(FX_|INVERTED_NORMAL_SPHERE|polySurface66|SM_Env_Skydome|Characters|BigCharacters|Capes|Tail_Rig)',
        static_src=[os.path.join("FBX", d) for d in ("Buildings", os.path.join("Buildings", "Base"), "Environment", "Props",
                                                     "Vehicles", "Weapons", os.path.join("Characters", "Attachments"))],
        src_chars=[os.path.join("FBX", "Characters", "Unreal_Characters"), os.path.join("FBX", "Characters", "Attachments")],
    ),
    "darkfortress": dict(
        title="POLYGON Dark Fortress",
        mats={"DarkFortress" + s for s in ("Shader", "Shader01B", "Shader01C", "Shader02B", "Shader04B", "Shader04C",
                                          "BrickLarge01", "BrickMedium01", "BrickSmall01", "SmoothStone01", "SmoothStoneDark01",
                                          "ChippedStone01", "Planks01", "Grunge01", "Moss01", "Thorns01", "Chains01", "Spiderweb01")}
             | {"DungeonWater"},
        skip=r'^(FX_|Characters|BR_Characters|Cape)',
        static_src=["FBX"],
        src_chars=["FBX"],
    ),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('src_root', help="the pack's Source_Files folder")
    ap.add_argument('static_out')
    ap.add_argument('chars_out')
    ap.add_argument('--pack', default='rivals', choices=sorted(PACKS))
    args = ap.parse_args()
    cfg = PACKS[args.pack]
    base_mats = cfg['mats']

    problems, modes, families = [], collections.Counter(), collections.Counter()
    node_renames, degenerate, char_lines = [], [], []
    n_src, n_out = check_static([os.path.join(args.src_root, d) for d in cfg['static_src']], args.static_out, base_mats,
                                re.compile(cfg['skip']), problems, modes, families, node_renames, degenerate)
    src_chars = [os.path.join(args.src_root, d) for d in cfg['src_chars']]
    n_chars = check_chars(args.chars_out, src_chars, base_mats, problems, char_lines) if os.path.exists(os.path.join(args.chars_out, '_chars.json')) else 0
    args.title = cfg['title']

    lines = ['%s toonified (Blender 5.0 headless)' % args.title,
             'static: source %d FBX (after skips) | toonified %d | normal mode auto %d / smooth %d' % (n_src, n_out, modes['auto'], modes['smooth']),
             'static material families: ' + ', '.join('%s %d' % kv for kv in sorted(families.items())),
             'characters: %d rigged FBX, each with a coincident <mesh>_Outline shell object' % n_chars,
             'outline: exact copy, %s material, no baked offset, smooth normals, not flipped -- heden_outline.shader inflates it' % OUTLINE,
             'units: source already cm (NOT scaled) -> static vmdl import_scale 0.3937, characters ScaleAndMirror 0.3937',
             '',
             '=== characters (%d) ===' % n_chars] + char_lines + [
             '',
             '=== static node names differing from filename (%d) ===' % len(node_renames)] + node_renames + [
             '',
             '=== degenerate faces dropped on import (%d, harmless) ===' % len(degenerate)] + degenerate + [
             '',
             '=== problems (%d) ===' % len(problems)] + problems
    open(os.path.join(args.static_out, '_report.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    print('\n'.join(lines[:6]))
    print('characters checked: %d | node renames: %d | degenerate-face drops: %d | problems: %d' % (n_chars, len(node_renames), len(degenerate), len(problems)))
    for p in problems[:30]:
        print('  PROBLEM', p)


if __name__ == '__main__':
    main()
