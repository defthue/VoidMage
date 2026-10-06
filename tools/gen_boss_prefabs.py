"""Write the Bone Colossus's three part prefabs (Assets/Prefabs/boss_skull.prefab, boss_hand_l.prefab, boss_hand_r.prefab).

    python tools/gen_boss_prefabs.py

Each is a network root (NetworkMode Object, tags enemy/npc/boss) with its renderer ON the root, so the sword's
physics query and a bolt's trace land on the same object the BossPart lives on:
    skull   ModelRenderer dungeonrealms/sm_env_hell_bone_skull_01 + BossPart(Skull) + BossBrain; children `jaw`
            (the jaw model, rotated by the brain about its hinge) and two eye point lights
    hand    SkinnedModelRenderer boss/hand_<side>.vmdl on animgraphs/boss_hand.vanmgrph + BossPart(HandL/R)
The BoxCollider is made by BossPart at runtime from the model bounds (or the BoxCenter/BoxSize below). Numbers here
are the placeholders from the plan; the prefabs are regenerated from this file, not hand-edited (gen_knight_prefab
convention). GUIDs are stable across runs.
"""
import json
import math
import os
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PREFABS = os.path.join(ROOT, "Assets", "Prefabs")
NS = uuid.UUID("7c4f2a91-3d5e-4b6a-8f10-2e9c7d1b5a33")

SKULL_MODEL = "dungeonrealms/sm_env_hell_bone_skull_01.vmdl"
JAW_MODEL = "dungeonrealms/sm_env_hell_bone_skull_jaw_01.vmdl"
HAND_GRAPH = "animgraphs/boss_hand.vanmgrph"

# the skull's box: the cranium without the horns (model bounds are 18 m wide with them); face is +x
SKULL_BOX = dict(BoxCenter="0,0,0", BoxSize="260,260,250")
# vitals are the prefab's fallback only: the brain writes the boss_hp_* convars over them at the wake-up
# (BossBrain.ApplyVitals); keep these in step with those defaults. The skull's GuardHealth is its first layer
# (phase 2 when it goes; both hands dead strikes it off). No posture/staggers (cut 2026-09-23).
SKULL = dict(Kind="Skull", MaxHealth=2150.0, GuardHealth=1350.0, HitSound="sounds/swordhitstone.sound", **SKULL_BOX)
HAND = dict(MaxHealth=600.0, HitSound="sounds/bonebreak.sound")
# stance distances in skull MODEL units (the brain multiplies by the skull's scale, 1.47x in the demo); fight
# distances (StanceDistance, ArenaRadius, speeds) in WORLD units since the player is their reference
BRAIN = dict(JawHinge="-24,0,20", JawOpenDegrees=32.0, HoverHeight=230.0, HoverBob=6.0, HoverBobHz=0.3,
                          StanceDistance=900.0, StanceSpeed=140.0, ArenaRadius=1600.0,
             HandHoverHeight=60.0, WakeSeconds=8.0)
# eye sockets in skull-local units (face +x): forward, +-width, up; the green fire is BossEyes (added by the brain)
EYES = {"eye_r": "68,-52,42", "eye_l": "68,52,42"}
EYE_LIGHT = "0.35,1,0.45,1"


def shape_specs(*parts):
    """The hit shapes of one or more entries of Assets/Boss/boss_shapes.json (tools/gen_boss_shapes.py) as the
    BossPart.ShapeSpecs lines `name|parent|cx,cy,cz|qx,qy,qz,qw|sx,sy,sz`; (entry, parent) pairs."""
    with open(os.path.join(ROOT, "Assets", "Boss", "boss_shapes.json")) as f:
        data = json.load(f)
    out = []
    for entry, parent in parts:
        for s in data[entry]:
            out.append("%s|%s|%s|%s|%s" % (s["name"], s.get("parent", parent), ",".join("%.2f" % v for v in s["center"]),
                                           ",".join("%.6f" % v for v in s["rot"]), ",".join("%.2f" % v for v in s["size"])))
    return out


def stance_rotation(side):
    """The hand's frame fix from its rig json: the quaternion (x,y,z,w) that turns the model so its palm normal
    points +x and its finger direction +z. The rig json is in the Blender import frame, which is the engine's
    model frame yawed by 90 degrees (the skull faces -y there and +x in the engine, verified by rendering), so the
    palm normal and finger direction are first brought into the model frame: (x, y, z) = (-y, x, z)_blender.
    The brain composes LookAt(palmToward, fingersToward) * this for every hand pose."""
    import numpy as np
    with open(os.path.join(ROOT, "Assets", "Boss", "hand_%s_rig.json" % side)) as f:
        rig = json.load(f)
    frame = np.array([[0.0, -1.0, 0.0], [1.0, 0.0, 0.0], [0.0, 0.0, 1.0]])
    n = frame @ np.array(rig["palm_normal"], dtype=float)
    d = frame @ np.array(rig["direction"], dtype=float)
    n /= np.linalg.norm(n)
    d = d - n * float(d @ n)
    d /= np.linalg.norm(d)
    left = np.cross(d, n)        # y = z cross x in the right-handed x-forward, y-left, z-up frame
    m = np.column_stack([n, left, d])   # model frame -> canonical: columns are where x, y, z go
    r = m.T                             # canonical <- model
    # matrix to quaternion (x, y, z, w)
    t = np.trace(r)
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        w, x, y, z = 0.25 * s, (r[2, 1] - r[1, 2]) / s, (r[0, 2] - r[2, 0]) / s, (r[1, 0] - r[0, 1]) / s
    elif r[0, 0] > r[1, 1] and r[0, 0] > r[2, 2]:
        s = math.sqrt(1.0 + r[0, 0] - r[1, 1] - r[2, 2]) * 2
        w, x, y, z = (r[2, 1] - r[1, 2]) / s, 0.25 * s, (r[0, 1] + r[1, 0]) / s, (r[0, 2] + r[2, 0]) / s
    elif r[1, 1] > r[2, 2]:
        s = math.sqrt(1.0 + r[1, 1] - r[0, 0] - r[2, 2]) * 2
        w, x, y, z = (r[0, 2] - r[2, 0]) / s, (r[0, 1] + r[1, 0]) / s, 0.25 * s, (r[1, 2] + r[2, 1]) / s
    else:
        s = math.sqrt(1.0 + r[2, 2] - r[0, 0] - r[1, 1]) * 2
        w, x, y, z = (r[1, 0] - r[0, 1]) / s, (r[0, 2] + r[2, 0]) / s, (r[1, 2] + r[2, 1]) / s, 0.25 * s
    return "%.6f,%.6f,%.6f,%.6f" % (x, y, z, w)


def guid(*parts):
    return str(uuid.uuid5(NS, "/".join(str(p) for p in parts)))


def events():
    return {"OnComponentDestroy": None, "OnComponentDisabled": None, "OnComponentEnabled": None,
            "OnComponentFixedUpdate": None, "OnComponentStart": None, "OnComponentUpdate": None}


def go(name, gid, tags="", pos="0,0,0", components=(), children=(), network=1):
    return {
        "__guid": gid, "__version": 2, "Flags": 0, "Name": name, "Position": pos, "Rotation": "0,0,0,1", "Scale": "1,1,1",
        "Tags": tags, "Enabled": True, "NetworkMode": network, "NetworkFlags": 0, "NetworkOrphaned": 0,
        "NetworkTransmit": True, "OwnerTransfer": 1, "NetworkInterpolation": True,
        "Components": list(components), "Children": list(children),
    }


def model_renderer(gid, model):
    return {"__type": "Sandbox.ModelRenderer", "__guid": gid, "__enabled": True, "Flags": 0,
            "BodyGroups": 18446744073709551615, "CreateAttachments": False, "LodOverride": None, "MaterialGroup": None,
            "MaterialOverride": None, "Materials": None, "Model": model,
            "RenderOptions": {"GameLayer": True, "OverlayLayer": False, "BloomLayer": False, "AfterUILayer": False},
            "RenderType": "On", "Tint": "1,1,1,1", **events()}


def skinned_renderer(gid, model, graph):
    return {"__type": "Sandbox.SkinnedModelRenderer", "__guid": gid, "__enabled": True, "Flags": 0,
            "AnimationGraph": graph, "BodyGroups": 18446744073709551615, "BoneMergeTarget": None,
            "CreateAttachments": False, "CreateBoneObjects": False, "LodOverride": None, "MaterialGroup": None,
            "MaterialOverride": None, "Materials": None, "Model": model, "Morphs": {},
            "Parameters": {"bools": {}, "ints": {}, "floats": {}, "vectors": {}, "rotations": {}},
            "PlaybackRate": 1, "RenderOptions": {"GameLayer": True, "OverlayLayer": False, "BloomLayer": False, "AfterUILayer": False},
            "RenderType": "On", "Sequence": {"Name": None, "Looping": True, "Blending": False}, "Tint": "1,1,1,1",
            "UseAnimGraph": True, **events()}


def component(gid, type_name, props):
    return {"__type": type_name, "__guid": gid, "__enabled": True, "Flags": 0, **events(), **props}


def point_light(gid, color, radius):
    return {"__type": "Sandbox.PointLight", "__guid": gid, "__enabled": True, "Flags": 0, "Attenuation": 1.0,
            "Contribution": "Diffuse, Specular, Transmissive", "FogMode": "Enabled", "FogStrength": 1,
            "LightColor": color, "Radius": radius, "ShadowBias": 0, "ShadowHardness": 0, "Shadows": False, **events()}


def prefab(root):
    return {"RootObject": root, "ResourceVersion": 2, "ShowInMenu": False, "MenuPath": None, "MenuIcon": None,
            "DontBreakAsTemplate": False, "__references": [], "__version": 2}


def write(name, root):
    root["__properties"] = {}
    root["__variables"] = []
    path = os.path.join(PREFABS, name + ".prefab")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(prefab(root), f, indent=2, ensure_ascii=False)
    print("wrote", path)


def main():
    eyes = [go(n, guid("skull", n), pos=p, components=[point_light(guid("skull", n, "light"), EYE_LIGHT, 200.0)], network=2)
            for n, p in EYES.items()]
    jaw = go("jaw", guid("skull", "jaw"), components=[model_renderer(guid("skull", "jaw", "r"), JAW_MODEL)], network=2)
    skull = go("boss_skull", guid("skull"), tags="enemy,npc,boss", components=[
        model_renderer(guid("skull", "r"), SKULL_MODEL),
        component(guid("skull", "part"), "VoidMage.Boss.BossPart", dict(ShapeSpecs=shape_specs(("skull", ""), ("jaw", "jaw")), **SKULL)),
        component(guid("skull", "brain"), "VoidMage.Boss.BossBrain", BRAIN),
    ], children=[jaw] + eyes)
    write("boss_skull", skull)
    for side, kind in (("l", "HandL"), ("r", "HandR")):
        with open(os.path.join(ROOT, "Assets", "Boss", "hand_%s_rig.json" % side)) as f:
            rig = json.load(f)
        wrist = rig["wrist_model"]
        elbow = rig["elbow_model"]
        palm = rig["palm_model"]
        d = [e - w for e, w in zip(elbow, wrist)]
        n = math.sqrt(sum(x * x for x in d)) or 1.0
        elbow_dir = [x / n for x in d]                  # the forearm mesh's own axis (the sculpt's bent wrist)
        fb = rig["direction"]                           # the finger direction, Blender frame -> model frame
        rest_dir = [fb[1], -fb[0], -fb[2]]              # a straight wrist: opposite the fingers
        vec = lambda v: ",".join("%.3f" % x for x in v)
        forearm = go("forearm", guid("hand", side, "forearm"), pos=vec(wrist),
                     components=[model_renderer(guid("hand", side, "forearm", "r"), "boss/hand_%s_forearm.vmdl" % side)], network=2)
        hand = go("boss_hand_" + side, guid("hand", side), tags="enemy,npc,boss", components=[
            skinned_renderer(guid("hand", side, "r"), "boss/hand_%s.vmdl" % side, HAND_GRAPH),
            component(guid("hand", side, "part"), "VoidMage.Boss.BossPart",
                      dict(Kind=kind, StanceRotation=stance_rotation(side), Pivot=vec(palm), ShapeSpecs=shape_specs(("hand_" + side, "")), **HAND)),
            component(guid("hand", side, "arm"), "VoidMage.Boss.BossArm", dict(Wrist=vec(wrist), ElbowDir=vec(elbow_dir), RestDir=vec(rest_dir), Pivot=vec(palm), Length=round(n * 1.05, 1))),
        ], children=[forearm])
        print("  stance", side, stance_rotation(side), "wrist", wrist, "elbow dir", [round(x, 3) for x in elbow_dir], "rest dir", [round(x, 3) for x in rest_dir], "palm", palm)
        write("boss_hand_" + side, hand)


if __name__ == "__main__":
    main()
