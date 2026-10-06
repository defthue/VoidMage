"""Animation-side readers on top of fbx_bounds.parse: per-bone translation ranges and key counts, so a vmdl
generator can decide which clips carry root motion (and a verification step can prove a retarget kept the
pelvis root-relative) without opening Blender.

    python tools/fbx_anim.py <clip.fbx> [bone ...]     (default bones: root pelvis)
"""
import os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbx_bounds import parse, find  # noqa: E402

FBX_TICKS_PER_SECOND = 46186158000.0


def bone_curves(path):
    """{(model name, property, component): (key times in seconds, key values)} for every animated Model."""
    _, nodes = parse(path)
    models, curves, curve_nodes = {}, {}, {}
    for objs in find(nodes, 'Objects'):
        for o in objs[2]:
            if o[0] == 'Model':
                models[o[1][0]] = o[1][1].decode('latin1').split('\x00')[0]
            elif o[0] == 'AnimationCurve':
                kt = kv = None
                for c in o[2]:
                    if c[0] == 'KeyTime':
                        kt = c[1][0]
                    elif c[0] == 'KeyValueFloat':
                        kv = c[1][0]
                if kt is not None and kv is not None:
                    curves[o[1][0]] = (tuple(t / FBX_TICKS_PER_SECOND for t in kt), tuple(kv))
            elif o[0] == 'AnimationCurveNode':
                curve_nodes[o[1][0]] = True
    node_model, curve_node = {}, {}
    for conns in find(nodes, 'Connections'):
        for c in conns[2]:
            if c[0] != 'C' or c[1][0] != b'OP':
                continue
            child, parent, prop = c[1][1], c[1][2], c[1][3].decode('latin1')
            if child in curve_nodes and parent in models:
                node_model[child] = (models[parent], prop)
            elif child in curves and parent in curve_nodes:
                curve_node[child] = (parent, prop)
    out = {}
    for cid, (nid, comp) in curve_node.items():
        if nid in node_model:
            model, prop = node_model[nid]
            out[(model, prop, comp)] = curves[cid]
    return out


def translation_range(curves, bone):
    """(max - min) per axis of the bone's Lcl Translation keys, or None when the bone has no keys."""
    axes = []
    for comp in ('d|X', 'd|Y', 'd|Z'):
        c = curves.get((bone, 'Lcl Translation', comp))
        if c is None:
            return None
        axes.append(max(c[1]) - min(c[1]))
    return tuple(axes)


def key_count(curves, bone):
    c = curves.get((bone, 'Lcl Translation', 'd|X')) or curves.get((bone, 'Lcl Rotation', 'd|X'))
    return len(c[0]) if c else 0


def root_travel(path, bone='root', threshold=1.0):
    """Largest per-axis translation range of `bone` in cm, and whether it exceeds `threshold`."""
    r = translation_range(bone_curves(path), bone)
    if r is None:
        return 0.0, False
    m = max(r)
    return m, m > threshold


if __name__ == '__main__':
    args = sys.argv[1:]
    path = args[0]
    bones = args[1:] or ['root', 'pelvis']
    cv = bone_curves(path)
    for b in bones:
        r = translation_range(cv, b)
        print('%-12s keys %4d  translation range %s' % (b, key_count(cv, b), None if r is None else tuple(round(v, 2) for v in r)))
