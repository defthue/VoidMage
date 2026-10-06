"""Minimal binary-FBX reader: prints UnitScaleFactor, each Model's Lcl Scaling and each
Geometry's vertex bounds, so the vmdl import_scale can be derived instead of guessed."""
import struct, zlib, sys

def read_props(data, pos, count):
    props = []
    for _ in range(count):
        t = data[pos:pos+1]; pos += 1
        if t == b'Y': props.append(struct.unpack_from('<h', data, pos)[0]); pos += 2
        elif t == b'C': props.append(bool(data[pos])); pos += 1
        elif t == b'I': props.append(struct.unpack_from('<i', data, pos)[0]); pos += 4
        elif t == b'F': props.append(struct.unpack_from('<f', data, pos)[0]); pos += 4
        elif t == b'D': props.append(struct.unpack_from('<d', data, pos)[0]); pos += 8
        elif t == b'L': props.append(struct.unpack_from('<q', data, pos)[0]); pos += 8
        elif t in (b'S', b'R'):
            n = struct.unpack_from('<I', data, pos)[0]; pos += 4
            props.append(data[pos:pos+n]); pos += n
        elif t in (b'f', b'd', b'l', b'i', b'b'):
            n, enc, clen = struct.unpack_from('<III', data, pos); pos += 12
            raw = data[pos:pos+clen]; pos += clen
            if enc == 1: raw = zlib.decompress(raw)
            fmt = {b'f': 'f', b'd': 'd', b'l': 'q', b'i': 'i', b'b': 'b'}[t]
            props.append(struct.unpack('<%d%s' % (n, fmt), raw))
        else:
            raise ValueError('unknown prop type %r at %d' % (t, pos))
    return props, pos

def read_node(data, pos, v64):
    if v64:
        end, nprops, plen = struct.unpack_from('<QQQ', data, pos); pos += 24
    else:
        end, nprops, plen = struct.unpack_from('<III', data, pos); pos += 12
    nlen = data[pos]; pos += 1
    name = data[pos:pos+nlen].decode('latin1'); pos += nlen
    if end == 0:
        return None, pos
    props, pos = read_props(data, pos, nprops)
    children = []
    while pos < end:
        child, pos = read_node(data, pos, v64)
        if child is None: break
        children.append(child)
    return (name, props, children), end

def parse(path):
    data = open(path, 'rb').read()
    version = struct.unpack_from('<I', data, 23)[0]
    v64 = version >= 7500
    pos = 27
    nodes = []
    while pos < len(data):
        node, pos = read_node(data, pos, v64)
        if node is None: break
        nodes.append(node)
    return version, nodes

def find(nodes, name):
    return [n for n in nodes if n[0] == name]

def props70(node):
    out = {}
    for c in node[2]:
        if c[0] == 'Properties70':
            for p in c[2]:
                if p[0] == 'P':
                    out[p[1][0].decode()] = p[1][4:]
    return out

def _euler_xyz(degrees):
    """Rotation matrix (row-major 3x3) for an FBX XYZ Euler in degrees."""
    import math
    cx, sx = math.cos(math.radians(degrees[0])), math.sin(math.radians(degrees[0]))
    cy, sy = math.cos(math.radians(degrees[1])), math.sin(math.radians(degrees[1]))
    cz, sz = math.cos(math.radians(degrees[2])), math.sin(math.radians(degrees[2]))
    rx = ((1, 0, 0), (0, cx, -sx), (0, sx, cx))
    ry = ((cy, 0, sy), (0, 1, 0), (-sy, 0, cy))
    rz = ((cz, -sz, 0), (sz, cz, 0), (0, 0, 1))
    def mul(a, b):
        return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))
    return mul(rz, mul(ry, rx))


def mesh_world_sizes(path):
    """{mesh Model name: (sx, sy, sz)} of each mesh's bind-pose extent in FBX WORLD axes, where y is up.

    Node scale is NOT decoration: Blender writes geometry in metres under a flat 100x node scale, s&box
    applies it, and a vmdl scale derived from the raw vertex numbers comes out 100x too big (as does one
    derived from Blender's own bound_box, which is the EVALUATED, armature-posed box, not the bind pose).

    Node ROTATION is not decoration either. Exporting with `bake_space_transform=False` leaves the geometry
    in Blender's own Z-up orientation and puts the axis conversion on the node as a -90 degrees X rotation,
    so the raw vertex extents are (width, DEPTH, HEIGHT) and reading index 1 as the height gets a quadruped
    exactly wrong -- the rat reported 66 units tall when that is its nose-to-tail length. The bounding box
    is therefore rotated properly rather than just scaled. (The largest extent is unaffected either way,
    which is why the vmdl scales were right all along and only the per-axis figures were wrong.)"""
    _, nodes = parse(path)
    models, geoms, parent, geom_of = {}, {}, {}, {}
    for objs in find(nodes, 'Objects'):
        for o in objs[2]:
            if o[0] == 'Model':
                p = props70(o)
                models[o[1][0]] = (o[1][1].decode('latin1').split('\x00')[0],
                                   p.get('Lcl Scaling', [1.0, 1.0, 1.0]), p.get('GeometricScaling', [1.0, 1.0, 1.0]),
                                   p.get('Lcl Rotation', [0.0, 0.0, 0.0]), p.get('PreRotation', [0.0, 0.0, 0.0]))
            elif o[0] == 'Geometry':
                for c in o[2]:
                    if c[0] == 'Vertices':
                        v = c[1][0]
                        geoms[o[1][0]] = tuple((min(v[i::3]), max(v[i::3])) for i in range(3))
    for conns in find(nodes, 'Connections'):
        for c in conns[2]:
            if c[0] == 'C' and c[1][0] == b'OO':
                child, par = c[1][1], c[1][2]
                if child in models:
                    parent[child] = par
                if child in geoms and par in models:
                    geom_of[par] = child
    out = {}
    for mid, (name, _, geometric, _, _) in models.items():
        if mid not in geom_of:
            continue
        # the 8 corners of the local box, through every node's scale then rotation up the parent chain
        lo_hi = geoms[geom_of[mid]]
        corners = [[lo_hi[0][x], lo_hi[1][y], lo_hi[2][z]]
                   for x in (0, 1) for y in (0, 1) for z in (0, 1)]
        for i in range(3):
            for c in corners:
                c[i] *= geometric[i]
        node = mid
        while node in models:
            _, scaling, _, rotation, prerotation = models[node]
            m = _euler_xyz([prerotation[i] + rotation[i] for i in range(3)])
            for c in corners:
                s = [c[i] * scaling[i] for i in range(3)]
                c[:] = [sum(m[i][k] * s[k] for k in range(3)) for i in range(3)]
            node = parent.get(node)
        out[name] = tuple(max(c[i] for c in corners) - min(c[i] for c in corners) for i in range(3))
    return out


def report(path):
    version, nodes = parse(path)
    print('==', path, 'version', version)
    for gs in find(nodes, 'GlobalSettings'):
        p = props70(gs)
        print('  UnitScaleFactor', p.get('UnitScaleFactor'), 'OriginalUnitScaleFactor', p.get('OriginalUnitScaleFactor'),
              'UpAxis', p.get('UpAxis'), 'FrontAxis', p.get('FrontAxis'), 'CoordAxis', p.get('CoordAxis'))
    for objs in find(nodes, 'Objects'):
        for o in objs[2]:
            if o[0] == 'Model':
                p = props70(o)
                print('  Model', o[1][1].decode('latin1').split('\x00')[0], 'scale', p.get('Lcl Scaling'), 'rot', p.get('Lcl Rotation'), 'trans', p.get('Lcl Translation'))
            elif o[0] == 'Geometry':
                nm = o[1][1].decode('latin1').split('\x00')[0]
                for c in o[2]:
                    if c[0] == 'Vertices':
                        v = c[1][0]
                        xs, ys, zs = v[0::3], v[1::3], v[2::3]
                        print('  Geometry', nm, 'verts', len(v)//3,
                              'bounds x[%.3f %.3f] y[%.3f %.3f] z[%.3f %.3f]' % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
                    if c[0] == 'LayerElementNormal':
                        pass
            elif o[0] == 'Material':
                print('  Material', o[1][1].decode('latin1').split('\x00')[0])

if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
