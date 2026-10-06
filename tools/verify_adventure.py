"""Verify the toonified Adventure pack against its Synty source, write _report.txt.

  python tools/verify_adventure.py <src_fbx_dir> <out_dir>

Checks, per file:
  * every source FBX produced an output
  * exactly two mesh nodes: SM_X and SM_X_Outline (node name, which may differ from the filename)
  * exactly two materials: AdventureShader + FantasyKingdomOutline
  * poly count preserved from the source, and base/outline geometry identical in count
  * the outline is COINCIDENT with the base (no baked offset -- heden_outline.shader does the
    inflation, so any positional drift here is a bug)
  * scale unchanged from source (Adventure source is already cm -- a x100 slip is the easy
    mistake here, so it is checked explicitly against the source extents)
"""
import sys, os, re, json, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbx_bounds import parse, find

SRC, OUT = sys.argv[1], sys.argv[2]
BASE_MAT = "AdventureShader"
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
    nodes = [n for n, _ in re.findall(r'([\x20-\x7E]{2,80})\x00\x01Model(?s:.{0,12}?)(Mesh|Null|LimbNode)', raw)]
    _, tree = parse(path)
    geos = []
    for objs in find(tree, 'Objects'):
        for g in objs[2]:
            if g[0] != 'Geometry':
                continue
            V = child(g, 'Vertices')[1][0]
            PI = child(g, 'PolygonVertexIndex')[1][0]
            npoly = sum(1 for i in PI if i < 0)
            lo = [min(V[k::3]) for k in range(3)]
            hi = [max(V[k::3]) for k in range(3)]
            geos.append((len(V) // 3, npoly, lo, hi))
    return mats, nodes, geos


def main():
    results = json.load(open(os.path.join(OUT, '_results.json'), encoding='utf-8'))
    src_files = sorted(f for f in os.listdir(SRC) if f.lower().endswith('.fbx'))
    out_files = sorted(f for f in os.listdir(OUT) if f.lower().endswith('.fbx'))

    problems = []
    modes = collections.Counter()
    node_renames = []

    missing = [f for f in src_files if f not in out_files]
    extra = [f for f in out_files if f not in src_files]
    if missing: problems.append('missing outputs: %s' % missing)
    if extra: problems.append('unexpected outputs: %s' % extra)

    for f in out_files:
        base = f[:-4]
        r = results.get(f, {})
        if not r.get('ok'):
            problems.append('%s: batch failure %s' % (f, r.get('error')))
            continue
        modes[r.get('mode', '?')] += 1
        try:
            mats, nodes, geos = scan(os.path.join(OUT, f))
            _, snodes, sgeos = scan(os.path.join(SRC, f))
        except Exception as e:
            problems.append('%s: unreadable (%r)' % (f, e))
            continue

        if sorted(set(mats)) != sorted([BASE_MAT, OUTLINE]):
            problems.append('%s: materials %s' % (f, sorted(set(mats))))

        node_base = [n for n in nodes if not n.endswith('_Outline')]
        if len(nodes) != 2 or len(node_base) != 1 or \
                sorted(nodes) != sorted([node_base[0], node_base[0] + '_Outline']):
            problems.append('%s: nodes %s' % (f, nodes))
            continue
        if node_base[0] != base:
            node_renames.append('%s -> node %s' % (f, node_base[0]))

        if len(geos) != 2:
            problems.append('%s: expected 2 geometries, got %d' % (f, len(geos)))
            continue
        if geos[0][1] != geos[1][1] or geos[0][0] != geos[1][0]:
            problems.append('%s: base/outline mismatch %s vs %s' % (f, geos[0][:2], geos[1][:2]))

        if sgeos and sgeos[0][1] != geos[0][1]:
            problems.append('%s: poly count changed %d -> %d' % (f, sgeos[0][1], geos[0][1]))

        # scale preserved (the x100 trap)
        if sgeos:
            se = [sgeos[0][3][k] - sgeos[0][2][k] for k in range(3)]
            oe = [geos[0][3][k] - geos[0][2][k] for k in range(3)]
            for k in range(3):
                if abs(se[k]) > 1e-6 and abs(oe[k] / se[k] - 1.0) > 0.001:
                    problems.append('%s: extent changed axis %d: %.3f -> %.3f (x%.2f)'
                                    % (f, k, se[k], oe[k], oe[k] / se[k]))
                    break

        # shell must be coincident with the base: no baked offset
        b, o = geos[0], geos[1]
        drift = max(max(abs(b[2][k] - o[2][k]), abs(b[3][k] - o[3][k])) for k in range(3))
        if drift > max(OFFSET, 1e-4) + 1e-4:
            problems.append('%s: outline drifts %.5f from base (expected %.3f)' % (f, drift, OFFSET))

    lines = []
    lines.append('POLYGON Adventure toonified -> voidmage AdventurePack format (Blender 5.0 headless)')
    lines.append('source: %d FBX | toonified %d | failures %d'
                 % (len(src_files), sum(modes.values()), sum(1 for p in problems if 'batch failure' in p)))
    lines.append('normal mode: auto (30 deg smoothing groups) %d | smooth %d' % (modes['auto'], modes['smooth']))
    lines.append('outline: separate <node>_Outline object, %s material, coincident with the base '
                 '(no baked offset), smooth normals, normals not flipped -- heden_outline.shader '
                 'inflates it' % OUTLINE)
    lines.append('units: source is already cm (NOT scaled, unlike DungeonPack) -> vmdl import_scale 0.3937')
    lines.append('material: single slot %s -> shaders/adventureshader.vmat (whole pack is one atlas)' % BASE_MAT)
    lines.append('')
    lines.append('=== node names differing from filename (%d, Synty naming, kept) ===' % len(node_renames))
    lines.extend(node_renames)
    lines.append('')
    lines.append('=== smooth-mode models (%d) ===' % modes['smooth'])
    lines.extend(sorted(f[:-4] for f in out_files if results.get(f, {}).get('mode') == 'smooth'))
    lines.append('')
    lines.append('=== problems (%d) ===' % len(problems))
    lines.extend(problems)
    open(os.path.join(OUT, '_report.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')

    print('\n'.join(lines[:6]))
    print('node renames:', len(node_renames), '| problems:', len(problems))
    for p in problems[:25]:
        print('  PROBLEM', p)


if __name__ == '__main__':
    main()
