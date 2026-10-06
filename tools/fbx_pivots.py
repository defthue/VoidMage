"""Print bounds + the position of the highest vertices for the kit pieces the dungeon builder
places, so pivots and stair climb direction are measured rather than guessed.
FBX space here: Y up, cm."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from fbx_bounds import parse, find

def geom_verts(path):
    _, nodes = parse(path)
    for objs in find(nodes, 'Objects'):
        for o in objs[2]:
            if o[0] == 'Geometry':
                nm = o[1][1].decode('latin1').split('\x00')[0]
                if nm.endswith('_Outline'): continue
                for c in o[2]:
                    if c[0] == 'Vertices':
                        v = c[1][0]
                        return [(v[i], v[i+1], v[i+2]) for i in range(0, len(v), 3)]
    return []

if __name__ == '__main__':
    for name in sys.argv[1:]:
        vs = geom_verts(name)
        xs, ys, zs = zip(*vs)
        print('== %s  (%d verts)' % (os.path.basename(name), len(vs)))
        print('   x[%.1f .. %.1f]  y[%.1f .. %.1f]  z[%.1f .. %.1f]' % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
        top = [v for v in vs if v[1] > max(ys) - 5]
        bot = [v for v in vs if v[1] < min(ys) + 5]
        if top and bot:
            cx = lambda L: (sum(v[0] for v in L) / len(L), sum(v[2] for v in L) / len(L))
            print('   top verts centre x,z = (%.1f, %.1f)   bottom verts centre x,z = (%.1f, %.1f)' % (*cx(top), *cx(bot)))
