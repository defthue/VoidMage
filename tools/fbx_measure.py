"""Extents of every SM_Env_* piece (cm, FBX Y-up), plus a doorway-opening check.
  python tools/fbx_measure.py Assets/DungeonPack            -> table of extents for wall/ceiling/tile/pillar/stairs pieces
  python tools/fbx_measure.py Assets/DungeonPack --doors    -> where the opening sits along each DoorFrame"""
import sys, os, glob
sys.path.insert(0, os.path.dirname(__file__))
from fbx_pivots import geom_verts

root = sys.argv[1]
doors = '--doors' in sys.argv
pat = 'SM_Env_Wall_DoorFrame*' if doors else 'SM_Env_*'
for f in sorted(glob.glob(os.path.join(root, pat + '.fbx'))):
    name = os.path.basename(f)[:-4]
    if not doors and not any(k in name for k in ('Wall', 'Ceiling', 'Tiles_0', 'Pillar', 'Stairs', 'Basement')):
        continue
    vs = geom_verts(f)
    if not vs: continue
    xs, ys, zs = zip(*vs)
    if doors:
        # mid-height slice: which x ranges have geometry (the gap is the opening)
        mid = sorted(v[0] for v in vs if 60 < v[1] < 180)
        cols = [round(x / 25) * 25 for x in mid]
        present = sorted(set(cols))
        gaps = []
        for a, b in zip(present, present[1:]):
            if b - a > 25: gaps.append((a, b))
        print('%-40s x[%.0f..%.0f] y[%.0f..%.0f] z[%.0f..%.0f]  opening gaps at mid height: %s' % (name, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs), gaps))
    else:
        print('%-42s x[%7.1f %7.1f] y[%7.1f %7.1f] z[%7.1f %7.1f]  size %5.0f x %5.0f x %5.0f' % (
            name, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs), max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
