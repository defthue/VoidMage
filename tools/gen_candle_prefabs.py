"""Writes Assets/Prefabs/Candles/*.prefab: table candles with a FireLight flame on every wick.

Wick tips were measured from the FBX geometry (scratch wicks.py, 2026-09-17): the thin mesh island on top of
each wax body, in s&box model units (X = fbx z, Y = fbx x, Z = fbx y, import_scale 0.3937). Candles without a
separate wick mesh (FantasyKingdom) use the wax top instead. Each prefab carries one point light above the
group and one flame tongue per wick through FireLight.TongueOffsets.
"""
import json, os, uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "Assets", "Prefabs", "Candles")

WICK_SINK = 0.3      # flame base this far below a wick tip
TOP_LIFT = 0.2       # flame base this far above a wick-less wax top
LIGHT_ABOVE = 4.0    # light this far above the highest flame base (the dungeon builder's candle rule)

# model -> (wick tips, has separate wick mesh)
CANDLES = {
    "dungeonpack/sm_prop_candle_01": ([(0.00, -0.10, 13.73)], True),
    "dungeonpack/sm_prop_candle_02": ([(0.05, 0.00, 17.62)], True),
    "dungeonpack/sm_prop_candles_01": ([(-1.09, -0.19, 14.83), (1.37, 1.21, 11.22), (1.04, -2.01, 10.79)], True),
    "dungeonpack/sm_prop_candles_02": ([(-0.67, -0.62, 13.76), (1.62, 0.67, 10.41), (1.32, -2.31, 10.01), (-1.08, 3.15, 8.61),
                                        (-1.18, -3.19, 8.09), (3.50, -1.58, 7.69), (-3.49, 0.12, 6.71)], True),
    "dungeonpack/sm_prop_candles_03": ([(-1.68, -0.29, 12.41), (2.10, 1.85, 9.83), (1.60, -3.08, 9.34)], True),
    "dungeonpack/sm_prop_candles_04": ([(-1.52, -2.13, 13.73), (-0.74, 2.61, 10.87), (3.16, -1.24, 10.33)], True),
    "darkfantasy/sm_prop_candle_01": ([(-0.10, -1.07, 15.51)], True),
    "darkfantasy/sm_prop_candle_02": ([(-0.10, -1.12, 10.00)], True),
    "darkfantasy/sm_prop_candle_03": ([(0.64, 0.26, 21.42)], True),
    "darkfantasy/sm_prop_candle_04": ([(0.25, 0.36, 15.66)], True),
    "darkfantasy/sm_prop_candle_05": ([(-0.79, -0.16, 12.86), (0.78, -1.04, 9.10), (0.57, 1.16, 6.81)], True),
    "darkfantasy/sm_prop_candle_06": ([(0.85, 1.53, 8.67), (0.97, -1.85, 5.76), (-1.86, -0.37, 5.76), (2.32, -0.07, 4.73)], True),
    "darkfantasy/sm_prop_candle_07": ([(0.24, 0.31, 5.76)], True),
    "darkfantasy/sm_prop_candle_08": ([(0.24, 0.25, 10.90)], True),
    "darkfantasy/sm_prop_candle_09": ([(0.26, 0.27, 11.52), (-0.49, 2.50, 9.50), (0.13, -2.21, 7.91), (0.80, -4.00, 6.98), (1.19, 1.34, 5.76)], True),
    "darkfortress/sm_prop_candle_01": ([(0.01, 0.31, 23.01)], True),
    "darkfortress/sm_prop_candle_02": ([(0.01, 0.35, 13.92)], True),
    "fantasykingdom/sm_item_candlestick_01": ([(0.02, -0.01, 19.31), (0.00, 4.84, 17.59), (0.00, -4.84, 16.25)], False),
    "fantasykingdom/sm_item_candlestick_02": ([(0.02, -0.01, 19.31)], False),
    "fantasykingdom/sm_item_candlestick_04": ([(0.00, 0.00, 16.24)], False),
    "fantasykingdom/sm_item_candlestick_05": ([(0.00, 0.00, 11.91)], False),
    "fantasykingdom/sm_item_candlestick_06": ([(0.02, -0.04, 60.88), (0.00, 7.30, 59.25), (0.00, -7.59, 58.20)], False),
    "fantasykingdom/sm_prop_candle_melted_group_01": ([(-1.94, 2.71, 6.84), (-1.46, -3.33, 5.94), (2.51, 0.22, 4.94)], False),
}

# lanterns: the candle stub inside the cage, measured by slicing the mesh (cage floor 6.6, stub cone tip 8.9,
# roof underside 13.7 above the base; lantern_02 is the same body 15.16 lower with a hanging hook on top).
# The flame base sits on the stub and the light stays inside the cage, so a hanging lantern lights below it.
LANTERN_OUT = os.path.join(ROOT, "Assets", "Prefabs", "Lanterns")
LANTERNS = {
    "dungeonpack/sm_prop_lantern_01": (0.0, 0.0, 8.0),
    "dungeonpack/sm_prop_lantern_02": (0.0, 0.0, 8.0 - 15.16),
}
LANTERN_LIGHT_ABOVE = 2.5   # light still under the roof
LANTERN_SCALE = 0.4         # < 0.42 adds the glass glow halo (FireVfxRig candle rule)

LIGHT_COLOR = (1.4, 1.05, 0.49)   # the builder's candle: (1, 0.75, 0.35) * 1.4
LIGHT_RADIUS = 150.0


def gid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "voidmage/candle/" + "/".join(parts)))


def vec(v):
    return ",".join("%g" % round(c, 3) for c in v)


def prefab(model, wicks, has_wick, name=None, bases=None, light_above=LIGHT_ABOVE, scale=None):
    name = name or "candle_" + model.replace("/", "_").replace("sm_prop_", "").replace("sm_item_", "")
    bases = bases or [(x, y, z - WICK_SINK if has_wick else z + TOP_LIFT) for x, y, z in wicks]
    if scale is None:
        scale = 0.45 if len(bases) == 1 else 0.42
    top = max(b[2] for b in bases)
    cx = sum(b[0] for b in bases) / len(bases)
    cy = sum(b[1] for b in bases) / len(bases)
    light_pos = (cx, cy, top + light_above)
    flame_base = (cx, cy, top)
    offsets = None if len(bases) == 1 else [vec(tuple(b[i] - flame_base[i] for i in range(3))) for b in bases]
    color = "%g,%g,%g,1" % LIGHT_COLOR
    none_events = {k: None for k in ("OnComponentDestroy", "OnComponentDisabled", "OnComponentEnabled",
                                     "OnComponentFixedUpdate", "OnComponentStart", "OnComponentUpdate")}
    return name, {
        "RootObject": {
            "__guid": gid(model, "root"), "__version": 2, "Flags": 0, "Name": name,
            "Position": "0,0,0", "Rotation": "0,0,0,1", "Scale": "1,1,1", "Tags": "", "Enabled": True,
            "NetworkMode": 2, "NetworkFlags": 0, "NetworkOrphaned": 0, "NetworkTransmit": True, "OwnerTransfer": 1,
            "Components": [{
                "__type": "Sandbox.ModelRenderer", "__guid": gid(model, "model"), "__enabled": True, "Flags": 0,
                "BodyGroups": 18446744073709551615, "CreateAttachments": False, "LodOverride": None,
                "MaterialGroup": None, "MaterialOverride": None, "Materials": None,
                "Model": model + ".vmdl", **none_events,
                "RenderOptions": {"GameLayer": True, "OverlayLayer": False, "BloomLayer": False, "AfterUILayer": False},
                # the light sits on the wick: a shadow-casting candle would throw a black cone under itself
                "RenderType": "Off", "Tint": "1,1,1,1",
            }],
            "Children": [{
                "__guid": gid(model, "light"), "__version": 2, "Flags": 0, "Name": "light",
                "Position": vec(light_pos), "Rotation": "0,0,0,1", "Scale": "1,1,1", "Tags": "light_point,light",
                "Enabled": True, "NetworkMode": 2, "NetworkFlags": 0, "NetworkOrphaned": 0, "NetworkTransmit": True,
                "OwnerTransfer": 1,
                "Components": [
                    {
                        "__type": "Sandbox.PointLight", "__guid": gid(model, "pointlight"), "__enabled": True, "Flags": 0,
                        "Attenuation": 1, "Contribution": "Diffuse, Specular, Transmissive", "FogMode": "Enabled",
                        "FogStrength": 1, "LightColor": color, **none_events, "Radius": LIGHT_RADIUS,
                        "ShadowBias": 0, "ShadowHardness": 0, "Shadows": True,
                    },
                    {
                        "__type": "VoidMage.Dungeon.FireLight", "__guid": gid(model, "firelight"), "__enabled": True, "Flags": 0,
                        "Amount": 0.25, "CastShadows": "Off", "BaseAttenuation": 1, "BaseColor": color, "BasePosition": vec(light_pos),
                        "BaseRadius": LIGHT_RADIUS, "BaseSet": True, "FlameCard": None, "FlameHeight": 1,
                        "FlameOffset": vec((0, 0, -light_above)), "FlameScale": scale,
                        "FlameWidth": 1, "Kind": "Candle", **none_events, "Phase": 0, "RandomLeanDirection": True,
                        "Seed": 0, "Smoke": False, "Sparks": False, "TempoVariation": 0.25, "Tint": "0,0,0,0",
                        "TongueOffsets": offsets, "TongueRadius": 0, "Tongues": 1, "WallFaceX": 0, "WallGlow": False,
                    },
                ],
                "Children": [],
            }],
            "__properties": {}, "__variables": [],
        },
        "ResourceVersion": 2, "ShowInMenu": False, "MenuPath": None, "MenuIcon": None,
        "DontBreakAsTemplate": False, "__references": [], "__version": 2,
    }


def main():
    os.makedirs(OUT, exist_ok=True)
    for model, (wicks, has_wick) in CANDLES.items():
        assert os.path.exists(os.path.join(ROOT, "Assets", model + ".vmdl")), model
        name, data = prefab(model, wicks, has_wick)
        with open(os.path.join(OUT, name + ".prefab"), "w", encoding="utf-8", newline="\n") as fh:
            json.dump(data, fh, indent=2)
        print("wrote", name, "(%d flame%s)" % (len(wicks), "" if len(wicks) == 1 else "s"))
    os.makedirs(LANTERN_OUT, exist_ok=True)
    for model, flame in LANTERNS.items():
        assert os.path.exists(os.path.join(ROOT, "Assets", model + ".vmdl")), model
        name = model.split("/")[-1].replace("sm_prop_", "")
        name, data = prefab(model, None, False, name=name, bases=[flame], light_above=LANTERN_LIGHT_ABOVE, scale=LANTERN_SCALE)
        with open(os.path.join(LANTERN_OUT, name + ".prefab"), "w", encoding="utf-8", newline="\n") as fh:
            json.dump(data, fh, indent=2)
        print("wrote", name)


if __name__ == "__main__":
    main()
