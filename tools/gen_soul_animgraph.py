"""Write Assets/animgraphs/tormented_soul.vanmgrph: the graph of the Tormented Soul (tormented_soul.vmdl), a flying
legless ghost on the skeleton rig. Same parameters as the skeleton graph so Code/Npc/SkeletonBody.cs drives both:

    loco        = 2D blend on (move_x, move_y): `jump_loop` hovering at the centre, the six `jump_in_move_loop_*`
                  directions at radius 0.5 AND 1 (no gaits: a flier moves or it hovers), `_f` again at y 1.5
    attacks     = `attack_trigger` + float `attack_kind`: 1 attack_b_03_a, 2 attack_b_03_c (head fixed by
                  tools/fix_clip_bones.py), 3 attack_c_03_a (the leaping opener), 4 unused -> attack_b_03_a
    hurts       = `hurt_trigger` + `hurt_kind` 0-3 light_hurt_f/b/l/r (no shield, so kinds 4/5 never come)
    dead        = bool `dead` -> die_01, no way out (no ragdoll: SkeletonBody.Ragdoll off)
    busy        = as the skeleton graph: one-shots also leave when the host drops it
`guard` exists so SkeletonBody's Set() has a target, nothing reads it. Every clip plays IN PLACE (the vmdl extracts
the root motion, SkeletonNpc.ApplyRootMotion is off): the code flies the body.

    python tools/gen_soul_animgraph.py
"""
import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from gen_fp_arms_animgraph import (sequence, root, cond_param, cond_finished, transition,  # noqa: E402
                                   state, state_machine, param_bool, param_float)
from gen_skeleton_animgraph import blend2d, cond_float, DIRS  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "animgraphs", "tormented_soul.vanmgrph")
MODEL = "characters/tormentedsoul/tormented_soul.vmdl"

ATTACK_SPEED = 1.15     # the user's middle ground (1.0 read a little slow, 1.35 too fast); SkeletonNpc's spec seconds are at this speed
LEAP_SPEED = 1.15
HURT_SPEED = 1.3
DEATH_SPEED = 1.0
RUN_RADIUS = 1.5

# ids: band 2500000xxx (unique inside this graph)
P_MX, P_MY, P_GUARD, P_ATK, P_AKIND, P_HURT, P_HKIND, P_DEAD, P_BUSY = range(2500000001, 2500000010)
N_ROOT, N_SM, N_LOCO, N_DEATH = range(2500000010, 2500000014)
N_ATTACK = {1: 2500000020, 2: 2500000021, 3: 2500000022, 4: 2500000023}
N_HURT = [2500000030 + k for k in range(4)]
S_LOCO, S_DEATH = 2500000050, 2500000051
S_ATTACK = {1: 2500000060, 2: 2500000061, 3: 2500000062, 4: 2500000063}
S_HURT = [2500000070 + k for k in range(4)]

ATTACK_CLIPS = {1: ("attack_b_03_a", ATTACK_SPEED), 2: ("attack_b_03_c", ATTACK_SPEED),
                3: ("attack_c_03_a", LEAP_SPEED), 4: ("attack_b_03_a", ATTACK_SPEED)}
HURT_CLIPS = [("light_hurt_f", HURT_SPEED), ("light_hurt_b", HURT_SPEED), ("light_hurt_l", HURT_SPEED), ("light_hurt_r", HURT_SPEED)]
DEATH_CLIP = "die_01"

BLEND_LOCO = 0.25
BLEND_ATTACK_IN, BLEND_ATTACK_OUT = 0.08, 0.2
BLEND_HURT_IN, BLEND_HURT_OUT = 0.05, 0.2
BLEND_DEAD = 0.1


def loco_items():
    items = [("jump_loop", (0.0, 0.0))]
    for radius in (0.5, 1.0):
        for d, (x, y) in DIRS:
            items.append((f"jump_in_move_loop_{d}", (x * radius, y * radius)))
    items.append(("jump_in_move_loop_f", (0.0, RUN_RADIUS)))
    return items


def interrupts():
    out = [transition([cond_param(P_DEAD, True)], S_DEATH, BLEND_DEAD)]
    out += [transition([cond_param(P_HURT, True), cond_float(P_HKIND, k)], S_HURT[k], BLEND_HURT_IN) for k in range(len(HURT_CLIPS))]
    return out


def attacks():
    return [transition([cond_param(P_ATK, True), cond_float(P_AKIND, kind)], S_ATTACK[kind], BLEND_ATTACK_IN) for kind in ATTACK_CLIPS]


def back_to_loco(blend):
    return [transition([cond_finished()], S_LOCO, blend), transition([cond_param(P_BUSY, False)], S_LOCO, blend)]


def main():
    nodes = [
        (N_LOCO, blend2d(N_LOCO, "loco", P_MX, P_MY, loco_items(), (-640, -256))),
        (N_DEATH, sequence(N_DEATH, "death", DEATH_CLIP, False, DEATH_SPEED, (-640, 640))),
    ]
    for kind, (clip, speed) in ATTACK_CLIPS.items():
        nodes.append((N_ATTACK[kind], sequence(N_ATTACK[kind], f"attack_{kind}", clip, False, speed, (-640, 64 + 64 * kind))))
    for k, (clip, speed) in enumerate(HURT_CLIPS):
        nodes.append((N_HURT[k], sequence(N_HURT[k], f"hurt_{k}", clip, False, speed, (-640, 384 + 64 * k))))

    states = [state(S_LOCO, "loco", N_LOCO, interrupts() + attacks(), True, (0, -256))]
    for kind in ATTACK_CLIPS:
        states.append(state(S_ATTACK[kind], f"attack_{kind}", N_ATTACK[kind], interrupts() + back_to_loco(BLEND_ATTACK_OUT), False, (0, 64 + 64 * kind)))
    for k in range(len(HURT_CLIPS)):
        states.append(state(S_HURT[k], f"hurt_{k}", N_HURT[k], interrupts() + attacks() + back_to_loco(BLEND_HURT_OUT), False, (512, 64 + 64 * k)))
    states.append(state(S_DEATH, "dead", N_DEATH, [], False, (256, 640)))

    nodes.append((N_SM, state_machine(N_SM, "TormentedSoul", states, (-96, 0))))
    nodes.append((N_ROOT, root(N_ROOT, N_SM, (256, 0))))
    nodes.sort(key=lambda n: n[0])

    params = "".join([
        param_float(P_MX, "move_x", -1.0, 1.0),
        param_float(P_MY, "move_y", -RUN_RADIUS, RUN_RADIUS),
        param_bool(P_GUARD, "guard"),
        param_bool(P_BUSY, "busy"),
        param_bool(P_ATK, "attack_trigger"),
        param_float(P_AKIND, "attack_kind", 0.0, 4.0),
        param_bool(P_HURT, "hurt_trigger"),
        param_float(P_HKIND, "hurt_kind", 0.0, 5.0),
        param_bool(P_DEAD, "dead"),
    ])

    out = f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:animgraph2:version{{0f7898b8-5471-45c4-9867-cd9c46bcfdb5}} -->
{{
	_class = "CAnimationGraph"
	m_nodeManager =
	{{
		_class = "CAnimNodeManager"
		m_nodes =
		[
{"".join(body for _, body in nodes)}		]
	}}
	m_pParameterList =
	{{
		_class = "CAnimParameterList"
		m_Parameters =
		[
{params}		]
	}}
	m_pTagManager =
	{{
		_class = "CAnimTagManager"
		m_tags = [  ]
	}}
	m_pMovementManager =
	{{
		_class = "CAnimMovementManager"
		m_MotorList =
		{{
			_class = "CAnimMotorList"
			m_motors = [  ]
		}}
		m_MovementSettings =
		{{
			_class = "CAnimMovementSettings"
			m_bShouldCalculateSlope = false
		}}
	}}
	m_pSettingsManager =
	{{
		_class = "CAnimGraphSettingsManager"
		m_settingsGroups =
		[
			{{
				_class = "CAnimGraphGeneralSettings"
				m_iGridSnap = 16
			}},
		]
	}}
	m_pActivityValuesList =
	{{
		_class = "CActivityValueList"
		m_activities = [  ]
	}}
	m_previewModels =
	[
		"{MODEL}",
	]
	m_boneMergeModels = [  ]
	m_cameraSettings =
	{{
		m_flFov = 60.0
		m_sLockBoneName = ""
		m_bLockCamera = false
		m_bViewModelCamera = false
	}}
}}
'''
    with open(OUT, "w", newline="\n") as f:
        f.write(out)
    print("wrote", OUT, "nodes", len(nodes), "states", len(states))


if __name__ == "__main__":
    main()
