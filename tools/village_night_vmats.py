#!/usr/bin/env python3
"""Give the village's outdoor materials both a daylight and a moonlit-night look, switched at runtime.

The village (Assets/scenes/adventure_demo.scene) is lit by its materials far more than by the sun:
every outdoor vmat carries `g_flAmbientFloor 0.5-0.75` (the shader default is 0.05), which is added
to the shadow side unconditionally, and `g_flSunColorInfluence 0.4`, which lets only 40% of the
DirectionalLight colour into the lit side. Recolouring the light alone therefore barely dims anything.

Since the main menu (2026-09-21) the village is sunny until Play and a stormy night after, in one
session, so the materials carry BOTH presets: the ordinary keys hold the day values, and the
`Toon Night` keys (heden_toon_core.hlsl: g_flNightResponse / g_flNightAmbientFloor /
g_flNightSunColorInfluence / ToonRampNight) hold the night ones. The scene-wide `VoidNight`
attribute (Code/Village/VillageSky.cs) fades between them.

    python tools/village_night_vmats.py            # day values + the night preset beside them
    python tools/village_night_vmats.py --revert   # day values only: the night keys are removed
    python tools/village_night_vmats.py --check    # report the current state, change nothing

Only the 14 vmats used by the two outdoor packs (AdventurePack, FantasyKingdom) are touched; the
outline vmats and everything the dungeon uses are left alone.
"""
import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SHADERS = ROOT / "Assets" / "Shaders"

# file -> the daylight ambient floor it shipped with (the Shader/Adventure pair were 0.75, the rest 0.5)
VILLAGE_VMATS = {
    "AdventureShader.vmat": "0.750",
    "FantasyKingdomShader.vmat": "0.750",
    "FantasyKingdomShaderB.vmat": "0.500",
    "FantasyKingdomShader3B.vmat": "0.500",
    "FantasyKingdomCastleRoof.vmat": "0.500",
    "FantasyKingdomCastleWall.vmat": "0.500",
    "FantasyKingdomEmissive.vmat": "0.500",
    "FantasyKingdomFlagSymbol.vmat": "0.500",
    "FantasyKingdomGlass.vmat": "0.500",
    "FantasyKingdomHorse1.vmat": "0.500",
    "FantasyKingdomHorse2.vmat": "0.500",
    "FantasyKingdomHorse3.vmat": "0.500",
    "FantasyKingdomPaintings.vmat": "0.500",
    "FantasyKingdomWheat.vmat": "0.500",
}

DAY_SUN = "0.400"
DAY_RAMP = "materials/heden_ramp_day.png"

# the night preset, in the shader's Toon Night keys; each is written after the day key it shadows
NIGHT = {
    "g_flNightResponse": ("1.000", "g_flSunColorInfluence"),
    "g_flNightAmbientFloor": ("0.120", "g_flSunColorInfluence"),
    "g_flNightSunColorInfluence": ("1.000", "g_flSunColorInfluence"),
    "ToonRampNight": ("materials/heden_ramp_night.png", "ToonRamp"),
}


def key_re(key: str) -> re.Pattern:
    return re.compile(r'^([ \t]*' + re.escape(key) + r'[ \t]+")([^"]*)(")[ \t]*$', re.M)


def set_key(text: str, key: str, value: str, changes: list, after: str = None) -> str:
    rx = key_re(key)
    m = rx.search(text)
    if m:
        if m.group(2) != value:
            changes.append(f"  {key}: {m.group(2)} -> {value}")
            text = rx.sub(lambda mm: mm.group(1) + value + mm.group(3), text, count=1)
        return text
    if after is None:
        changes.append(f"  MISSING {key}")
        return text
    anchor = key_re(after).search(text)
    if not anchor:
        changes.append(f"  MISSING {after} (wanted to add {key} after it)")
        return text
    indent = re.match(r"[ \t]*", anchor.group(1)).group(0)
    changes.append(f"  {key}: added {value}")
    return text[: anchor.end()] + f'\n{indent}{key} "{value}"' + text[anchor.end():]


def remove_key(text: str, key: str, changes: list) -> str:
    rx = re.compile(r'^[ \t]*' + re.escape(key) + r'[ \t]+"[^"]*"[ \t]*\r?\n', re.M)
    if rx.search(text):
        changes.append(f"  {key}: removed")
        text = rx.sub("", text, count=1)
    return text


def apply(path: Path, day_floor: str, with_night: bool, dry: bool) -> list:
    text = path.read_text(encoding="utf-8")
    out = text
    changes = []
    out = set_key(out, "g_flAmbientFloor", day_floor, changes)
    out = set_key(out, "g_flSunColorInfluence", DAY_SUN, changes)
    out = set_key(out, "ToonRamp", DAY_RAMP, changes)
    # reversed so keys sharing an anchor land in the order written above
    for key, (value, after) in reversed(list(NIGHT.items())):
        out = set_key(out, key, value, changes, after=after) if with_night else remove_key(out, key, changes)
    out = out.replace("// night preset applied 2026-09-15 (tools/village_night_vmats.py)", "// day + night presets (tools/village_night_vmats.py)")
    if not dry and out != text:
        path.write_text(out, encoding="utf-8")
    return changes


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--revert", action="store_true", help="day values only, no night keys")
    ap.add_argument("--check", action="store_true", help="report only")
    args = ap.parse_args()

    missing = [f for f in VILLAGE_VMATS if not (SHADERS / f).exists()]
    if missing:
        print("not found:", ", ".join(missing))
        return 1

    touched = 0
    for name, day_floor in VILLAGE_VMATS.items():
        changes = apply(SHADERS / name, day_floor, with_night=not args.revert, dry=args.check)
        if changes:
            touched += 1
            print(name)
            print("\n".join(changes))
    mode = "check" if args.check else ("revert" if args.revert else "day+night")
    print(f"{mode}: {touched} of {len(VILLAGE_VMATS)} files {'would change' if args.check else 'changed'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
