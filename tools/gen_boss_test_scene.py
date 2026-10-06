"""Write Assets/scenes/boss_test.scene: the boss fight's own test scene (no village, no run).

    python tools/gen_boss_test_scene.py [--prefab prefabs/hell_arena.prefab] [--no-instance]

The scene holds a camera (with the RetroFilter the arena's fog rides on), a `Boss Test` object carrying
Code/Boss/BossTestStage.cs (adopts the stage, spawns boss + player, bakes nav, wakes the boss), and an INSTANCE of
the stage prefab at the origin named `hell_arena`, so the stage shows in the editor and edits to the prefab show
up here. `--no-instance` leaves the stage out: the component then clones `boss_arena_prefab` at play.

A prefab instance in a .scene is `__Prefab` + `__PrefabInstancePatch` (root Position/Rotation overrides) +
`__PrefabIdToInstanceId`, a fresh instance guid for EVERY guid in the prefab (objects and components), the shape
adventure_demo.scene's torchstick instances have. Guids are stable across re-runs (uuid5).
"""
import argparse
import json
import os
import uuid

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
OUT = os.path.join(ROOT, "Assets", "scenes", "boss_test.scene")
NS = uuid.UUID("6f1c2b7a-9d3e-4c58-8a17-2e5b0d9c4f61")


def guid(*parts):
    return str(uuid.uuid5(NS, "/".join(str(p) for p in parts)))


def events():
    return {k: None for k in ("OnComponentDestroy", "OnComponentDisabled", "OnComponentEnabled", "OnComponentFixedUpdate", "OnComponentStart", "OnComponentUpdate")}


def go(name, gid, components, pos="0,0,0", rot="0,0,0,1"):
    return {
        "__guid": gid, "__version": 2, "Flags": 0, "Name": name, "Position": pos, "Rotation": rot, "Scale": "1,1,1",
        "Tags": "", "Enabled": True, "NetworkMode": 2, "NetworkFlags": 0, "NetworkOrphaned": 0, "NetworkTransmit": True,
        "OwnerTransfer": 1, "Components": components, "Children": [],
    }


def comp(type_name, gid, **props):
    c = {"__type": type_name, "__guid": gid, "__enabled": True, "Flags": 0}
    c.update(events())
    c.update(props)
    return c


def all_guids(node, out):
    if isinstance(node, dict):
        g = node.get("__guid")
        if isinstance(g, str):
            out.append(g)
        for v in node.values():
            all_guids(v, out)
    elif isinstance(node, list):
        for v in node:
            all_guids(v, out)


def prefab_instance(prefab_path):
    with open(os.path.join(ROOT, "Assets", prefab_path.replace("/", os.sep)), "r", encoding="utf-8") as f:
        prefab = json.load(f)
    root = prefab.get("RootObject") or prefab
    root_guid = root["__guid"]
    ids = []
    all_guids(root, ids)
    seen = set()
    id_map = {}
    for g in ids:
        if g in seen:
            continue
        seen.add(g)
        id_map[g] = guid("instance", prefab_path, g)
    return {
        "__guid": id_map[root_guid], "__version": 2,
        "__Prefab": prefab_path,
        "__PrefabInstancePatch": {
            "AddedObjects": [], "RemovedObjects": [],
            "PropertyOverrides": [
                {"Target": {"Type": "GameObject", "IdValue": root_guid}, "Property": "Position", "Value": "0,0,0"},
                {"Target": {"Type": "GameObject", "IdValue": root_guid}, "Property": "Rotation", "Value": "0,0,0,1"},
                {"Target": {"Type": "GameObject", "IdValue": root_guid}, "Property": "Name", "Value": "hell_arena"},
            ],
            "MovedObjects": [],
        },
        "__PrefabIdToInstanceId": id_map,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--prefab", default="prefabs/boss_stage.prefab")
    ap.add_argument("--no-instance", action="store_true")
    args = ap.parse_args()

    info = go("Scene Information", guid("info"), [comp("Sandbox.SceneInformation", guid("info", "c"), Author=None, Changes=None, Description="The Bone Colossus's test scene: press play, the boss wakes.", Group=None, SceneTags="", Title="boss_test", Version=None)])
    camera = go("Camera", guid("camera"), [
        comp("Sandbox.CameraComponent", guid("camera", "cam"), BackgroundColor="0.05,0.02,0.02,1", ClearFlags="All", EnablePostProcessing=True, FieldOfView=74, FovAxis="Vertical", IsMainCamera=True, Orthographic=False, OrthographicHeight=1204, PostProcessAnchor=None, Priority=1, RenderExcludeTags="", RenderTags="", RenderTexture=None, TargetEye="None", Viewport="0,0,1,1", ZFar=30000, ZNear=10),
        comp("Sandbox.Bloom", guid("camera", "bloom"), __version=1, Mode="Additive", Strength=0.5, Threshold=1.25, Tint="1,1,1,1"),
        comp("Sandbox.Tonemapping", guid("camera", "tone"), __version=3, AutoExposureEnabled=False, ExposureCompensation=0, ExposureMethod="Luminance", MaximumExposure=2, MinimumExposure=1, Mode="ACES", Rate=1),
        comp("VoidMage.Rendering.RetroFilter", guid("camera", "retro"), AnimateDither=False, ApplyStage="BeforePostProcess", BitsBlue=5, BitsGreen=5, BitsRed=5, Blend=1, Brightness=1, ChromaticAberration=0, ColorDepth=True, Contrast=1, Crt=False, Curvature=0, CustomHeight=400, DebugSplit=0, DepthEdges=False, DepthFog=False, Dither="Bayer4x4", DitherScale=1, DitherStrength=1, Duotone=0, EdgeColor="0,0,0,1", EdgeStrength=1, EdgeThreshold=0.06, FogAffectsSky=True, FogBeforeTransparents=True, FogColor="0,0,0,1", FogCurve=2, FogEnd=6000, FogMax=1, FogSealEdges=True, FogStart=3000, FogSteps=0, Gamma=1, Grading=True, HighlightColor="1,0.9,0.7,1", HitAberration=8, HitRipple=True, LiftColor="0.3,0.35,0.55,1", LiftRange=0.06, LockDitherToView=False, LowResolution=True, LuminanceSteps=14, Mask="None", MaskStrength=0.5, OverHud="Curvature", Palette=None, PaletteColors=0, PaletteDistance="Perceptual", PixelAspect=1.33, PixelGridStrength=0, PixelGridWidth=1, QuantizeInGamma=True, QuantizeMode="Rgb", Resolution="P480", RippleSeconds=0.25, RippleSpeed=1.6, RippleStrength=8, RippleWidth=26, SampleQuality="Quad", Saturation=1, ScanlineHardness=0, ScanlinePitch="LowResRow", ScanlineStrength=0, ShadowColor="0.05,0.02,0.12,1", ShadowLift=0.015, Tint="1,1,1,1", Upscale="Nearest", VignetteRadius=0.8, VignetteRoundness=0.5, VignetteSoftness=0.6, VignetteStrength=0),
    ], pos="1500,0,400", rot="0,0.2588,0,0.9659")
    test = go("Boss Test", guid("test"), [comp("VoidMage.Boss.BossTestStage", guid("test", "c"), Stage=None, PlayerPrefab={"_type": "gameobject", "prefab": "prefabs/player.prefab"}, WakeOnStart=True, WakeDelay=3, HellMaterials=True)])
    objects = [info, camera, test]
    if not args.no_instance:
        objects.append(prefab_instance(args.prefab))

    scene = {
        "__guid": guid("scene"),
        "GameObjects": objects,
        "SceneProperties": {
            "NetworkInterpolation": True, "PhysicsMode": "Physics3D", "TimeScale": 1, "WantsSystemScene": True,
            "Metadata": {"Title": "boss_test"},
            "NavMesh": {"Enabled": True, "IncludeStaticBodies": True, "IncludeKeyframedBodies": True, "EditorAutoUpdate": False, "AgentHeight": 64, "AgentRadius": 12, "AgentStepSize": 18, "AgentMaxSlope": 40, "ExcludedBodies": "", "IncludedBodies": "", "DeferGeneration": False, "CustomBounds": False},
            "GameObjectSystems": {"Sandbox.SelectionSetsSystem": {"Data": {"SelectionSets": []}}},
        },
        "ResourceVersion": 3, "Title": "boss_test", "Description": None, "__references": [], "__version": 3,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(scene, f, indent=2)
    print(f"wrote {OUT}: {len(objects)} objects" + ("" if args.no_instance else f", stage instance of {args.prefab}"))


if __name__ == "__main__":
    main()
