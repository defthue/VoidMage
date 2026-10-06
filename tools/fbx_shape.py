"""Cross-section and face-direction analysis of a kit piece (cm, FBX Y-up, pivot at origin).
  python tools/fbx_shape.py Assets/DungeonPack/SM_Env_Wall_End_Coner_Outer_01.fbx [...]
Prints: bounds; per axis-direction the total face area and where those faces sit; and an ASCII
cross-section of the footprint at three heights (5 cm cells)."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from fbx_bounds import parse, find

def geometry(path):
    _, nodes = parse(path)
    for objs in find(nodes, 'Objects'):
        for o in objs[2]:
            if o[0] != 'Geometry': continue
            nm = o[1][1].decode('latin1').split('\x00')[0]
            if nm.endswith('_Outline'): continue
            verts = idx = None
            for c in o[2]:
                if c[0] == 'Vertices': verts = c[1][0]
                if c[0] == 'PolygonVertexIndex': idx = c[1][0]
            vs = [(verts[i], verts[i + 1], verts[i + 2]) for i in range(0, len(verts), 3)]
            polys, cur = [], []
            for i in idx:
                if i < 0: cur.append(-i - 1); polys.append(cur); cur = []
                else: cur.append(i)
            return vs, polys
    return [], []

def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])

for path in sys.argv[1:]:
    vs, polys = geometry(path)
    xs, ys, zs = zip(*vs)
    print('==', os.path.basename(path), 'x[%.0f..%.0f] y[%.0f..%.0f] z[%.0f..%.0f]' % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    # face directions (side faces only: |normal.y| small), area-weighted position
    dirs = {'+x': [0, 0, 0, 0], '-x': [0, 0, 0, 0], '+z': [0, 0, 0, 0], '-z': [0, 0, 0, 0], '+y': [0, 0, 0, 0], '-y': [0, 0, 0, 0]}
    for p in polys:
        if len(p) < 3: continue
        # fan triangulation area-weighted normal
        n = [0.0, 0.0, 0.0]
        for i in range(1, len(p) - 1):
            c = cross(sub(vs[p[i]], vs[p[0]]), sub(vs[p[i + 1]], vs[p[0]]))
            n = [n[0] + c[0], n[1] + c[1], n[2] + c[2]]
        area = (n[0]**2 + n[1]**2 + n[2]**2) ** 0.5 / 2
        if area < 1e-6: continue
        nn = [v / (2 * area) for v in n]
        ax = max(range(3), key=lambda k: abs(nn[k]))
        key = ('+' if nn[ax] > 0 else '-') + 'xyz'[ax]
        cx = sum(vs[i][0] for i in p) / len(p); cz = sum(vs[i][2] for i in p) / len(p)
        d = dirs[key]; d[0] += area; d[1] += cx * area; d[2] += cz * area; d[3] += 1
    for k, (a, wx, wz, cnt) in dirs.items():
        if a > 0: print('   faces %s: area %7.0f cm2 in %3d polys, area-weighted centre x=%6.1f z=%6.1f' % (k, a, cnt, wx / a, wz / a))
    # cross sections
    for h in (30, 250, 470):
        band = [v for v in vs if abs(v[1] - h) < 40]
        if not band: continue
        x0, x1 = min(xs), max(xs); z0, z1 = min(zs), max(zs)
        step = 5.0
        cols = int((x1 - x0) / step) + 1; rows = int((z1 - z0) / step) + 1
        grid = [[' '] * cols for _ in range(rows)]
        for x, y, z in band:
            grid[int((z - z0) / step)][int((x - x0) / step)] = '#'
        # mark the pivot
        pi, pj = int((0 - z0) / step), int((0 - x0) / step)
        if 0 <= pi < rows and 0 <= pj < cols and grid[pi][pj] == ' ': grid[pi][pj] = '+'
        print('   section at y~%d (rows: z from %.0f down to %.0f; cols: x from %.0f to %.0f; + = pivot)' % (h, z1, z0, x0, x1))
        for row in reversed(grid): print('     |' + ''.join(row) + '|')
