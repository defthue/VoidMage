"""Write Assets/animgraphs/swordsman.vanmgrph: the graph the SpiritDemon and the Dark Lord share (kinds spirit_demon /
dark_lord, tools/skeleton_kinds.py), built on Synty's Sword Animations pack (Assets/Animations/SkeletonSword,
retarget_synty.py) with the Sword & Shield walk / jog / run for locomotion (the pack has none; user's pick, 2026-09-13).

The knight graph's layout without the guard states (no shield): `loco` = the radial Blend2D of the Sword & Shield
walk_* / jog_* / run with the pack's own `idle_base` sword stance at the centre; TEN attack states, one per
`attack_*_rootmotion` clip (the lunges are real root motion, ExtractMotion in the vmdl), `attack_kind` 1..10 in
ATTACK_CLIPS order = the prefab table's order (gen_swordsman_prefab.py asserts it); every attack flows into any other
on a new pulse (combos: SkeletonNpc.TryCombo). Hurt kinds 0-3 are the pack's directional hit_*_react; kind 4 is a
placeholder (the guard never rises: GuardPoints 0); kind 5, the posture break, is the pack's KNOCKDOWN as a chain
of three states -- knockdown_begin -> knockdown_loop -> knockdown_end, each stepping on `finished`, all leaving on
`busy == false` -- so the body stays down for the prefab's StaggerSeconds (the chain's length). Death is a ragdoll.
Attacks play at 1.0: the table's seconds are measured at 1.0 (tools/measure_sword_clips.py).

    python tools/gen_swordsman_animgraph.py
"""
import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from gen_fp_arms_animgraph import (sequence, root, cond_param, cond_finished, transition,  # noqa: E402
                                   state, state_machine, param_bool, param_float)
from gen_skeleton_animgraph import loco_items, blend2d, cond_float, RUN_RADIUS  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "animgraphs", "swordsman.vanmgrph")
MODEL = "characters/spiritdemon/spirit_demon.vmdl"

ATTACK_SPEED = 1.0
HURT_SPEED = 1.2        # hit_*_react 0.83 s -> 0.69 s, the skeletons' HurtSeconds
KNOCKDOWN_SPEED = 1.0

# kind N = ATTACK_CLIPS[N-1]; gen_swordsman_prefab.py's table is in this order
ATTACK_CLIPS = ["attack_lightcombo01a_rootmotion", "attack_lightcombo01b_rootmotion", "attack_lightcombo01c_rootmotion",
                "attack_lightfencing01_rootmotion",
                "attack_heavycombo01a_rootmotion", "attack_heavycombo01b_rootmotion", "attack_heavycombo01c_rootmotion",
                "attack_heavystab01_rootmotion", "attack_heavyflourish01_rootmotion",
                "attack_lightleaping01_rootmotion"]
# hurt_kind 0..5 enter HURT_CLIPS[0..5]; 6 and 7 are the knockdown chain's tail, reached only by `finished`
HURT_CLIPS = [("hit_f_react", HURT_SPEED), ("hit_b_react", HURT_SPEED), ("hit_l_react", HURT_SPEED), ("hit_r_react", HURT_SPEED),
              ("hit_f_stagger", 1.0),
              ("knockdown_begin_rootmotion", KNOCKDOWN_SPEED), ("knockdown_loop", KNOCKDOWN_SPEED), ("knockdown_end", KNOCKDOWN_SPEED)]
HURT_KINDS = 6
KD_BEGIN, KD_LOOP, KD_END = 5, 6, 7

# ---------------------------------------------------------------- ids (band 2800000xxx, unique)
P_MX, P_MY, P_GUARD, P_ATK, P_AKIND, P_HURT, P_HKIND = range(2800000001, 2800000008)
N_ROOT, N_SM, N_LOCO = range(2800000010, 2800000013)
P_BUSY = 2800000016
KINDS = list(range(1, len(ATTACK_CLIPS) + 1))
N_ATTACK = {k: 2800000100 + k for k in KINDS}
S_ATTACK = {k: 2800000200 + k for k in KINDS}
N_HURT = [2800000030 + k for k in range(len(HURT_CLIPS))]
S_LOCO = 2800000050
S_HURT = [2800000070 + k for k in range(len(HURT_CLIPS))]

BLEND_LOCO = 0.2
BLEND_ATTACK_IN, BLEND_ATTACK_OUT = 0.08, 0.2
BLEND_HURT_IN, BLEND_HURT_OUT = 0.05, 0.2
BLEND_CHAIN = 0.08


def interrupts():
    return [transition([cond_param(P_HURT, True), cond_float(P_HKIND, k)], S_HURT[k], BLEND_HURT_IN) for k in range(HURT_KINDS)]


def attacks(exclude=None):
    return [transition([cond_param(P_ATK, True), cond_float(P_AKIND, k)], S_ATTACK[k], BLEND_ATTACK_IN) for k in KINDS if k != exclude]


def back_to_loco(blend):
    """Leave a one-shot when the clip ends OR when the host says it is no longer busy (recovery cut short)."""
    return [transition([cond_finished()], S_LOCO, blend), transition([cond_param(P_BUSY, False)], S_LOCO, blend)]


def main():
    nodes = [(N_LOCO, blend2d(N_LOCO, "loco", P_MX, P_MY, loco_items("", "idle_base", "run"), (-640, -256)))]
    for k in KINDS:
        nodes.append((N_ATTACK[k], sequence(N_ATTACK[k], f"attack_{k}", ATTACK_CLIPS[k - 1], False, ATTACK_SPEED, (-640, 64 + 64 * k))))
    for k, (clip, speed) in enumerate(HURT_CLIPS):
        nodes.append((N_HURT[k], sequence(N_HURT[k], f"hurt_{k}", clip, False, speed, (-960, 64 + 64 * k))))

    states = [state(S_LOCO, "loco", N_LOCO, interrupts() + attacks(), True, (0, -256))]
    for k in KINDS:
        # a combo: the next attack's pulse lands while this state plays (a different kind, so the held pulse of
        # this one can never re-enter it)
        states.append(state(S_ATTACK[k], f"attack_{k}", N_ATTACK[k], interrupts() + attacks(exclude=k) + back_to_loco(BLEND_ATTACK_OUT),
                            False, (0, 64 + 64 * k)))
    for k in range(KD_BEGIN):
        states.append(state(S_HURT[k], f"hurt_{k}", N_HURT[k], interrupts() + attacks() + back_to_loco(BLEND_HURT_OUT), False, (512, 128 + 64 * k)))
    # the knockdown chain: down, lie, get up. A hit while down is a flinch (SkeletonNpc), never a new hurt pulse, so
    # the chain only ever steps forward; `busy == false` (the stagger's end) is the way out of every link
    states.append(state(S_HURT[KD_BEGIN], "knockdown_begin", N_HURT[KD_BEGIN], interrupts() + attacks() + [
        transition([cond_finished()], S_HURT[KD_LOOP], BLEND_CHAIN),
        transition([cond_param(P_BUSY, False)], S_LOCO, BLEND_HURT_OUT),
    ], False, (768, 128)))
    states.append(state(S_HURT[KD_LOOP], "knockdown_loop", N_HURT[KD_LOOP], interrupts() + attacks() + [
        transition([cond_finished()], S_HURT[KD_END], BLEND_CHAIN),
        transition([cond_param(P_BUSY, False)], S_LOCO, 0.25),
    ], False, (768, 192)))
    states.append(state(S_HURT[KD_END], "knockdown_end", N_HURT[KD_END], interrupts() + attacks() + [
        transition([cond_finished()], S_LOCO, 0.25),
        transition([cond_param(P_BUSY, False)], S_LOCO, 0.25),
    ], False, (768, 256)))

    nodes.append((N_SM, state_machine(N_SM, "Swordsman", states, (-96, 0))))
    nodes.append((N_ROOT, root(N_ROOT, N_SM, (256, 0))))
    nodes.sort(key=lambda n: n[0])

    params = "".join([
        param_float(P_MX, "move_x", -1.0, 1.0),
        param_float(P_MY, "move_y", -RUN_RADIUS, RUN_RADIUS),
        param_bool(P_GUARD, "guard"),   # SkeletonBody sets it every frame; nothing reads it (no shield)
        param_bool(P_BUSY, "busy"),
        param_bool(P_ATK, "attack_trigger"),
        param_float(P_AKIND, "attack_kind", 0.0, float(len(ATTACK_CLIPS))),
        param_bool(P_HURT, "hurt_trigger"),
        param_float(P_HKIND, "hurt_kind", 0.0, float(HURT_KINDS - 1)),
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
    print("wrote", OUT, "nodes", len(nodes), "states", len(states), "attack kinds", len(KINDS))


if __name__ == "__main__":
    main()
