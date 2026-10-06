"""Give every heden toon material a night ramp slot.

    python tools/fix_vmat_night_ramp.py [--dry]

heden_toon_core.hlsl declares `ToonRampNight` (the village's day/night lerp); a .vmat without the key makes the
engine log "Texture g_tToonRampNight doesn't exist in <vmat>" on load and the material goes flat white at night.
A vmat with the key EMPTY (`ToonRampNight ""`, which the editor's material inspector writes) is worse: the
compiler reaches for materials/default/default.tga, which is not in this project, and the whole material fails
(the flat error texture; RealmsShader.vmat, 2026-09-22). This inserts `ToonRampNight "materials/heden_ramp_night.png"`
after the ToonRamp line (or at the end of Layer0) in every vmat whose shader includes the core, and repairs empty ones.
"""
import argparse
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")
SHADERS = os.path.join(ASSETS, "Shaders")
RAMP = "materials/heden_ramp_night.png"


def core_shaders():
    out = set()
    for f in os.listdir(SHADERS):
        if f.endswith(".shader") and "heden_toon_core" in open(os.path.join(SHADERS, f), encoding="utf-8", errors="ignore").read():
            out.add(f.lower())
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry", action="store_true")
    args = ap.parse_args()
    shaders = core_shaders()
    added = fixed = 0
    for root, _dirs, files in os.walk(ASSETS):
        for f in files:
            if not f.lower().endswith(".vmat"):
                continue
            path = os.path.join(root, f)
            text = open(path, encoding="utf-8", errors="ignore").read()
            m = re.search(r'shader\s+"([^"]+)"', text)
            if not m or os.path.basename(m.group(1)).lower() not in shaders:
                continue
            new = text
            if re.search(r'^\s*ToonRampNight\s+""\s*$', new, re.M):
                new = re.sub(r'^(\s*)ToonRampNight\s+""\s*$', r'\1ToonRampNight "%s"' % RAMP, new, flags=re.M)
                fixed += 1
            elif not re.search(r'^\s*ToonRampNight\s+"', new, re.M):
                ramp = re.search(r'^(\s*)ToonRamp\s+"[^"]*"\s*$', new, re.M)
                if ramp:
                    line = '%sToonRampNight "%s"\n' % (ramp.group(1), RAMP)
                    new = new[:ramp.end()] + "\n" + line.rstrip("\n") + new[ramp.end():]
                else:
                    close = new.rstrip().rfind("}")
                    if close < 0:
                        continue
                    new = new[:close] + '\tToonRampNight "%s"\n' % RAMP + new[close:]
                added += 1
            if new != text and not args.dry:
                open(path, "w", encoding="utf-8", newline="\n").write(new)
    print("%s%d vmats given a night ramp, %d empty slots repaired" % ("(dry) " if args.dry else "", added, fixed))


if __name__ == "__main__":
    main()
