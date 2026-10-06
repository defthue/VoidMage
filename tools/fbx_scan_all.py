"""Scan every FBX: model node names, Lcl Scaling, geometry bounds, material slot names -> JSON."""
import sys, os, glob, json
sys.path.insert(0, os.path.dirname(__file__))
from fbx_bounds import parse, find, props70

root, out = sys.argv[1], sys.argv[2]
res = {}
for f in sorted(glob.glob(os.path.join(root, '*.fbx'))):
    version, nodes = parse(f)
    entry = {'models': [], 'geoms': [], 'materials': []}
    for objs in find(nodes, 'Objects'):
        for o in objs[2]:
            nm = o[1][1].decode('latin1').split('\x00')[0] if len(o[1]) > 1 else ''
            if o[0] == 'Model':
                p = props70(o)
                entry['models'].append({'name': nm, 'scale': list(p.get('Lcl Scaling', [1, 1, 1]))})
            elif o[0] == 'Geometry':
                for c in o[2]:
                    if c[0] == 'Vertices':
                        v = c[1][0]
                        xs, ys, zs = v[0::3], v[1::3], v[2::3]
                        entry['geoms'].append({'name': nm, 'n': len(v) // 3,
                                               'min': [min(xs), min(ys), min(zs)], 'max': [max(xs), max(ys), max(zs)]})
            elif o[0] == 'Material':
                entry['materials'].append(nm)
    res[os.path.basename(f)] = entry
json.dump(res, open(out, 'w'), indent=1)

# summary
from collections import Counter
sc = Counter()
for f, e in res.items():
    for m in e['models']:
        sc[tuple(round(x, 3) for x in m['scale'])] += 1
print('model scale histogram:', dict(sc))
print('files whose model scale != 1:')
for f, e in res.items():
    s = {tuple(round(x, 3) for x in m['scale']) for m in e['models']}
    if s != {(1.0, 1.0, 1.0)}:
        ext = max(max(g['max'][i] - g['min'][i] for i in range(3)) for g in e['geoms']) if e['geoms'] else 0
        print('  %-45s scale=%s  max_extent=%.3f  models=%s' % (f, s, ext, [m['name'] for m in e['models']]))
print('files with no outline model:', [f for f, e in res.items() if not any(m['name'].endswith('_Outline') for m in e['models'])])
print('files whose base model name != filename stem:')
for f, e in res.items():
    stem = f[:-4]
    base = [m['name'] for m in e['models'] if not m['name'].endswith('_Outline')]
    if base != [stem]:
        print('  %-45s -> %s' % (f, base))
print('small-extent files (max extent < 50 units, i.e. probably metres):')
for f, e in res.items():
    ext = max(max(g['max'][i] - g['min'][i] for i in range(3)) for g in e['geoms']) if e['geoms'] else 0
    if ext < 50:
        print('  %-45s ext=%.3f' % (f, ext))
